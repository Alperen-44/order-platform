using System.Globalization;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using NotificationService.Domain;
using NotificationService.Persistence;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;

namespace NotificationService.Messaging;

/// <summary>
/// Saga'nın sonuçlarını müşteriye bildirir. Gerçek bir e-posta/SMS sağlayıcısı yerine bildirimi
/// loglar ve veritabanına kaydeder (GET /notifications ile görülebilir).
///
/// processed_events sayesinde aynı olay iki kez gelse bile müşteriye iki e-posta gitmez.
/// </summary>
public sealed class NotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger<NotificationConsumer> logger)
    : KafkaConsumerService<NotificationDbContext>(scopeFactory, kafkaOptions, producer, logger)
{
    protected override string[] SubscribedTopics => [Topics.OrderEvents, Topics.PaymentEvents];
    protected override string GroupId => "notification-service";

    protected override async Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct)
    {
        Notification? notification = message.EventType switch
        {
            EventTypes.OrderConfirmed => Confirmed(message.Deserialize<OrderConfirmed>().Payload),
            EventTypes.OrderCancelled => Cancelled(message.Deserialize<OrderCancelled>().Payload),
            EventTypes.PaymentRefunded => Refunded(message.Deserialize<PaymentRefunded>().Payload),
            _ => null
        };

        if (notification is null) return;

        var db = services.GetRequiredService<NotificationDbContext>();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("[simulated email] to customer {CustomerId}: {Message}",
            notification.CustomerId, notification.Message);
    }

    private static Notification Confirmed(OrderConfirmed e) => new(
        e.OrderId, e.CustomerId, EventTypes.OrderConfirmed,
        $"Siparişiniz onaylandı. Tutar: {Money(e.TotalAmount)} TL");

    private static Notification Cancelled(OrderCancelled e) => new(
        e.OrderId, e.CustomerId, EventTypes.OrderCancelled,
        $"Siparişiniz iptal edildi. Sebep: {e.Reason}");

    private static Notification Refunded(PaymentRefunded e) => new(
        e.OrderId, e.CustomerId, EventTypes.PaymentRefunded,
        $"Ödemeniz iade edildi: {Money(e.Amount)} TL");

    // Docker imajının kültür ayarlarından bağımsız, her ortamda aynı çıktı: 3000.00
    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
