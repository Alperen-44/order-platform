using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderPlatform.Messaging.Consuming;

/// <summary>
/// Kafka tüketicileri için temel sınıf. Alt sınıf yalnızca topic, consumer group ve
/// mesajın nasıl işleneceğini söyler. Döngü, idempotency, retry, DLQ ve offset yönetimi burada.
///
/// Her mesaj için:
///   1. Transaction aç
///   2. processed_events'te bu eventId var mı? Varsa atla (tekrar gelen mesaj)
///   3. HandleAsync: iş mantığı (aynı DbContext, aynı transaction)
///   4. processed_events'e kaydet, commit et
///   5. Kafka offset'ini commit et
/// Hata olursa: zehirli mesaj → hemen DLQ; geçici hata → 5 deneme, sonra DLQ.
/// </summary>
public abstract class KafkaConsumerService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger logger) : BackgroundService
    where TDbContext : DbContext
{
    private const int MaxAttempts = 5;

    protected ILogger Logger { get; } = logger;

    protected abstract string Topic { get; }
    protected abstract string GroupId { get; }

    /// <summary>Her consumer group'un kendi DLQ'su var: hata o tüketiciye ait.</summary>
    public static string DeadLetterTopicFor(string groupId) => $"{groupId}.dlq";

    /// <summary>
    /// İş mantığı. <paramref name="services"/> üzerinden alınan TDbContext, idempotency kaydıyla
    /// aynı transaction'ı paylaşır; burada ayrıca commit etmeye gerek yok.
    /// </summary>
    protected abstract Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct);

    // Consume() bloklayan bir çağrı; uygulamanın açılışını tutmaması için ayrı bir thread'de çalıştırıyoruz.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offset'i mesajı işledikten SONRA elle commit ediyoruz.
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(Topic);
        Logger.LogInformation("Consumer group {GroupId} subscribed to {Topic}", GroupId, Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    Logger.LogWarning("Consume error on {Topic}: {Reason}", Topic, ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                if (result?.Message is null) continue;

                await HandleWithRetryAsync(result, ct);

                // Buraya ancak mesaj işlendiğinde ya da DLQ'ya güvenle yazıldığında geliyoruz.
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleWithRetryAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var message = ConsumedEvent.From(result);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ProcessOnceAsync(message, ct);
                return;
            }
            catch (PoisonMessageException ex)
            {
                // Tekrar denemenin anlamı yok: doğrudan DLQ.
                await SendToDeadLetterAsync(result, message, ex, attempt, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= MaxAttempts)
                {
                    await SendToDeadLetterAsync(result, message, ex, attempt, ct);
                    return;
                }

                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)); // 400ms, 800ms, 1.6s, 3.2s
                Logger.LogWarning(ex, "Handling {EventType} {EventId} failed (attempt {Attempt}/{Max}), retrying in {Delay}",
                    message.EventType, message.EventId, attempt, MaxAttempts, delay);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task ProcessOnceAsync(ConsumedEvent message, CancellationToken ct)
    {
        if (!Guid.TryParse(message.EventId, out var eventId))
            throw new PoisonMessageException($"Missing or invalid '{MessageHeaders.EventId}' header: '{message.EventId}'");

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var alreadyProcessed = await db.Set<ProcessedEvent>()
            .AnyAsync(p => p.ConsumerGroup == GroupId && p.EventId == eventId, ct);
        if (alreadyProcessed)
        {
            Logger.LogInformation("Skipping duplicate {EventType} {EventId} for {GroupId}",
                message.EventType, eventId, GroupId);
            return; // transaction'da değişiklik yok; dispose edilince geri alınır
        }

        await HandleAsync(message, scope.ServiceProvider, ct);

        // İki consumer aynı olayı aynı anda işlemeye kalkarsa birincil anahtar (consumer_group, event_id)
        // ikincisini durdurur; o deneme hata alır, tekrar denendiğinde "duplicate" olarak atlanır.
        db.Set<ProcessedEvent>().Add(new ProcessedEvent(GroupId, eventId, message.EventType));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task SendToDeadLetterAsync(
        ConsumeResult<string, string> result, ConsumedEvent message, Exception error, int attempts, CancellationToken ct)
    {
        var dlqTopic = DeadLetterTopicFor(GroupId);

        // Orijinal başlıkları koru, hatanın nedenini ve mesajın nereden geldiğini ekle.
        var headers = new Headers();
        if (result.Message.Headers is not null)
        {
            foreach (var header in result.Message.Headers)
                headers.Add(header.Key, header.GetValueBytes());
        }

        var errorText = $"{error.GetType().Name}: {error.Message}";
        if (errorText.Length > 1000) errorText = errorText[..1000];

        headers.Add("dlq-original-topic", Encoding.UTF8.GetBytes(result.Topic));
        headers.Add("dlq-original-partition", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()));
        headers.Add("dlq-original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()));
        headers.Add("dlq-consumer-group", Encoding.UTF8.GetBytes(GroupId));
        headers.Add("dlq-attempts", Encoding.UTF8.GetBytes(attempts.ToString()));
        headers.Add("dlq-error", Encoding.UTF8.GetBytes(errorText));
        headers.Add("dlq-failed-at", Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O")));

        // DLQ'ya yazılamadıysa offset'i commit edemeyiz (mesaj kaybolur). Kafka geri gelene kadar dene.
        while (true)
        {
            try
            {
                await producer.ProduceAsync(dlqTopic, new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = headers
                }, ct);

                Logger.LogError(error,
                    "Sent {EventType} {EventId} from {Position} to {DeadLetterTopic} after {Attempts} attempt(s)",
                    message.EventType, message.EventId, message.Position, dlqTopic, attempts);
                return;
            }
            catch (ProduceException<string, string> ex)
            {
                Logger.LogWarning("Could not write to {DeadLetterTopic} ({Reason}), retrying", dlqTopic, ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
