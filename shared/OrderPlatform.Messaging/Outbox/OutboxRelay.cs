using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderPlatform.Messaging.Outbox;

public sealed class OutboxRelayOptions
{
    public required string Topic { get; init; }
    public int BatchSize { get; init; } = 100;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// Outbox tablosundaki gönderilmemiş olayları okuyup Kafka'ya gönderen arka plan servisi.
///
/// Garanti: "en az bir kez" (at-least-once). Kafka'ya gönderip veritabanına "gönderildi"
/// yazamadan çökersek olay tekrar gönderilir. Bu yüzden tüketiciler idempotent olmak zorunda.
/// </summary>
public sealed class OutboxRelay<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IProducer<string, string> producer,
    OutboxRelayOptions options,
    ILogger<OutboxRelay<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox relay started, publishing to topic {Topic}", options.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Veritabanı ya da Kafka geçici olarak erişilemez olabilir; bir sonraki turda tekrar dene.
                logger.LogError(ex, "Outbox relay iteration failed, will retry");
            }

            // Dolu bir batch geldiyse arkasında daha fazlası olabilir: beklemeden devam et.
            if (published < options.BatchSize)
            {
                try { await Task.Delay(options.PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED: Servisin birden fazla kopyası çalışırsa her kopya FARKLI
        // satırları kilitler. Aynı olayı iki kopyanın birden göndermesi engellenir.
        var batchSize = options.BatchSize;
        var messages = await db.Set<OutboxMessage>()
            .FromSql($"""
                SELECT * FROM outbox
                WHERE published_at IS NULL
                ORDER BY created_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        var published = 0;
        foreach (var message in messages)
        {
            try
            {
                // Anahtar = aggregateId (orderId). Aynı siparişin bütün olayları aynı
                // partition'a düşer ve Kafka bu sırayı korur.
                await producer.ProduceAsync(options.Topic, new Message<string, string>
                {
                    Key = message.AggregateId.ToString(),
                    Value = message.Payload,
                    Headers = new Headers
                    {
                        { MessageHeaders.EventType, Encoding.UTF8.GetBytes(message.EventType) },
                        { MessageHeaders.EventId, Encoding.UTF8.GetBytes(message.Id.ToString()) }
                    }
                }, ct);

                message.MarkPublished(DateTimeOffset.UtcNow);
                published++;
            }
            catch (ProduceException<string, string> ex)
            {
                // Sırayı bozmamak için batch'in geri kalanını göndermeyi bırak.
                // Gönderilemeyen olaylar outbox'ta kalır ve bir sonraki turda tekrar denenir.
                logger.LogWarning("Kafka unavailable ({Reason}); {Remaining} event(s) stay in outbox",
                    ex.Error.Reason, messages.Count - published);
                break;
            }
        }

        // Kafka'ya giden olayların işaretini kaybetmemek için kapanış sinyalini burada dinlemiyoruz.
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);

        if (published > 0)
            logger.LogInformation("Published {Count} event(s) to {Topic}", published, options.Topic);

        return published;
    }
}
