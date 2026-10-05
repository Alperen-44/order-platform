using Microsoft.Extensions.Options;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;
using OrderService.Domain;
using OrderService.Persistence;

namespace OrderService.Messaging;

/// <summary>
/// inventory.events topic'ini dinler ve saga'yı ilerletir:
///   StockReserved          → sipariş StockReserved olur (Faz 4'te sırada ödeme var)
///   StockReservationFailed → sipariş Cancelled olur
/// </summary>
public sealed class InventoryEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    ILogger<InventoryEventsConsumer> logger)
    : KafkaConsumerService(scopeFactory, kafkaOptions, logger)
{
    protected override string Topic => Topics.InventoryEvents;
    protected override string GroupId => "order-service";

    protected override Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<OrderDbContext>();

        switch (message.EventType)
        {
            case EventTypes.StockReserved:
            {
                var payload = message.Deserialize<StockReserved>().Payload;
                return ApplyAsync(db, payload.OrderId, message, order => order.MarkStockReserved(), ct);
            }
            case EventTypes.StockReservationFailed:
            {
                var payload = message.Deserialize<StockReservationFailed>().Payload;
                return ApplyAsync(db, payload.OrderId, message, order => order.Cancel(payload.Reason), ct);
            }
            default:
                return Task.CompletedTask;
        }
    }

    private async Task ApplyAsync(
        OrderDbContext db, Guid orderId, ConsumedEvent message, Func<Order, bool> transition, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync(new object[] { orderId }, ct);
        if (order is null)
        {
            Logger.LogWarning("{EventType} received for unknown order {OrderId}; ignoring", message.EventType, orderId);
            return;
        }

        var previous = order.Status;
        if (!transition(order))
        {
            // Tekrar gelen ya da sırası geçmiş olay: durum zaten ilerlemiş, dokunma.
            Logger.LogInformation("{EventType} ignored for order {OrderId} in status {Status}",
                message.EventType, orderId, order.Status);
            return;
        }

        await db.SaveChangesAsync(ct);
        Logger.LogInformation("Order {OrderId}: {Previous} -> {Current}", orderId, previous, order.Status);
    }
}
