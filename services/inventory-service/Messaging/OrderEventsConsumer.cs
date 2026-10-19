using Confluent.Kafka;
using InventoryService.Features.Reservations;
using InventoryService.Persistence;
using Microsoft.Extensions.Options;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;

namespace InventoryService.Messaging;

/// <summary>
/// order.events topic'ini dinler:
///   OrderCreated   → stoğu rezerve et, sonucu (StockReserved / StockReservationFailed) yayınla
///   OrderConfirmed → rezervasyonu kesinleştir
///   OrderCancelled → TELAFİ: rezervasyonu geri bırak, stok tekrar satılabilir olsun
/// İş mantığı, outbox kaydı ve processed_events kaydı tek transaction'da yazılır.
/// </summary>
public sealed class OrderEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger<OrderEventsConsumer> logger)
    : KafkaConsumerService<InventoryDbContext>(scopeFactory, kafkaOptions, producer, logger)
{
    protected override string[] SubscribedTopics => [Topics.OrderEvents];
    protected override string GroupId => "inventory-service";

    protected override async Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct)
    {
        var reservations = services.GetRequiredService<StockReservationService>();

        switch (message.EventType)
        {
            case EventTypes.OrderCreated:
            {
                var envelope = message.Deserialize<OrderCreated>();
                var order = envelope.Payload;
                var lines = order.Items.Select(i => new ReserveLine(i.ProductId, i.Quantity)).ToList();

                var result = await reservations.ReserveAsync(
                    order.OrderId, lines, publishEvents: true, envelope.CorrelationId, ct);
                Logger.LogInformation("Order {OrderId}: stock reservation {Outcome}", order.OrderId, result.Outcome);
                break;
            }
            case EventTypes.OrderConfirmed:
            {
                var orderId = message.Deserialize<OrderConfirmed>().Payload.OrderId;
                var committed = await reservations.CommitAsync(orderId, ct);
                Logger.LogInformation("Order {OrderId}: {Count} reservation(s) committed", orderId, committed);
                break;
            }
            case EventTypes.OrderCancelled:
            {
                var cancelled = message.Deserialize<OrderCancelled>().Payload;
                var released = await reservations.ReleaseAsync(cancelled.OrderId, ct);
                Logger.LogInformation("Order {OrderId} cancelled ({Reason}): stock {Result}",
                    cancelled.OrderId, cancelled.Reason, released is null ? "was never reserved" : "released");
                break;
            }
            // PaymentRequested gibi Inventory'nin ilgilenmediği olaylar atlanır.
        }
    }
}
