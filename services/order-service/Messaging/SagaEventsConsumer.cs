using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;
using OrderPlatform.Messaging.Outbox;
using OrderService.Domain;
using OrderService.Persistence;

namespace OrderService.Messaging;

/// <summary>
/// Saga'nın orkestratörü. Inventory ve Payment'tan gelen sonuçlara göre siparişi bir sonraki
/// adıma taşır ve sıradaki adımı başlatan olayı outbox'a yazar:
///
///   StockReserved          → StockReserved durumu  + PaymentRequested  (tutar Inventory fiyatlarıyla)
///   StockReservationFailed → Cancelled             + OrderCancelled
///   PaymentCompleted       → Confirmed             + OrderConfirmed
///   PaymentFailed          → Cancelled             + OrderCancelled  (telafi: stok geri bırakılır)
///
/// Durum değişikliği, outbox kaydı ve processed_events kaydı tek transaction'da yazılır.
/// </summary>
public sealed class SagaEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger<SagaEventsConsumer> logger)
    : KafkaConsumerService<OrderDbContext>(scopeFactory, kafkaOptions, producer, logger)
{
    protected override string[] SubscribedTopics => [Topics.InventoryEvents, Topics.PaymentEvents];
    protected override string GroupId => "order-service";

    protected override Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<OrderDbContext>();

        switch (message.EventType)
        {
            case EventTypes.StockReserved:
            {
                var e = message.Deserialize<StockReserved>().Payload;
                var unitPrices = e.Items.ToDictionary(i => i.ProductId, i => i.UnitPrice);
                return ApplyAsync(db, e.OrderId, message,
                    order => order.MarkStockReserved(unitPrices), OrderOutboxEvents.PaymentRequested, ct);
            }
            case EventTypes.StockReservationFailed:
            {
                var e = message.Deserialize<StockReservationFailed>().Payload;
                return ApplyAsync(db, e.OrderId, message,
                    order => order.Cancel($"Stock: {e.Reason}"), OrderOutboxEvents.Cancelled, ct);
            }
            case EventTypes.PaymentCompleted:
            {
                var e = message.Deserialize<PaymentCompleted>().Payload;
                return ApplyAsync(db, e.OrderId, message,
                    order => order.Confirm(), OrderOutboxEvents.Confirmed, ct);
            }
            case EventTypes.PaymentFailed:
            {
                var e = message.Deserialize<PaymentFailed>().Payload;
                return ApplyAsync(db, e.OrderId, message,
                    order => order.Cancel($"Payment: {e.Reason}"), OrderOutboxEvents.Cancelled, ct);
            }
            default:
                // PaymentRefunded gibi Order'ın tepki vermediği olaylar.
                return Task.CompletedTask;
        }
    }

    private async Task ApplyAsync(
        OrderDbContext db,
        Guid orderId,
        ConsumedEvent message,
        Func<Order, bool> transition,
        Func<Order, OutboxMessage> nextEvent,
        CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null)
        {
            Logger.LogWarning("{EventType} received for unknown order {OrderId}; ignoring", message.EventType, orderId);
            return;
        }

        var previous = order.Status;
        if (!transition(order))
        {
            // Geç ya da sırası karışmış olay. Örnek: zaman aşımıyla iptal edilen siparişe sonradan
            // PaymentCompleted gelmesi. Durumu değiştirmiyoruz; alınan ödemeyi Payment servisi,
            // OrderCancelled olayını gördüğünde zaten iade ediyor.
            Logger.LogInformation("{EventType} ignored for order {OrderId} in status {Status}",
                message.EventType, orderId, order.Status);
            return;
        }

        db.OutboxMessages.Add(nextEvent(order));

        // Commit'i KafkaConsumerService yapıyor (processed_events kaydıyla birlikte).
        // Aynı anda zaman aşımı servisi de bu siparişi değiştirdiyse burada
        // DbUpdateConcurrencyException alınır; mesaj tekrar denenir ve güncel durumla işlenir.
        await db.SaveChangesAsync(ct);
        Logger.LogInformation("Order {OrderId}: {Previous} -> {Current}", orderId, previous, order.Status);
    }
}
