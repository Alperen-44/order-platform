using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrderPlatform.Messaging.Consuming;

/// <summary>
/// Kafka tüketicileri için temel sınıf. Alt sınıf yalnızca topic, consumer group ve
/// mesajın nasıl işleneceğini söyler; döngü, retry ve offset yönetimi burada.
/// </summary>
public abstract class KafkaConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    ILogger logger) : BackgroundService
{
    private const int MaxAttempts = 5;

    protected ILogger Logger { get; } = logger;

    protected abstract string Topic { get; }
    protected abstract string GroupId { get; }

    /// <summary>Her mesaj kendi DI scope'unda (kendi DbContext'iyle) işlenir.</summary>
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
            // Offset'i mesajı başarıyla işledikten SONRA elle commit ediyoruz.
            // Otomatik commit açık olsaydı, işlerken çöktüğümüzde mesaj kaybolabilirdi.
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

                var message = ConsumedEvent.From(result);
                await HandleWithRetryAsync(message, ct);
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

    private async Task HandleWithRetryAsync(ConsumedEvent message, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await HandleAsync(message, scope.ServiceProvider, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= MaxAttempts)
                {
                    // Faz 3'te bu mesaj bir Dead Letter Queue'ya gönderilecek.
                    Logger.LogError(ex,
                        "Giving up on {EventType} {EventId} at {Position} after {Attempts} attempts",
                        message.EventType, message.EventId, message.Position, attempt);
                    return;
                }

                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)); // 400ms, 800ms, 1.6s, 3.2s
                Logger.LogWarning(ex, "Handling {EventType} {EventId} failed (attempt {Attempt}), retrying in {Delay}",
                    message.EventType, message.EventId, attempt, delay);
                await Task.Delay(delay, ct);
            }
        }
    }
}
