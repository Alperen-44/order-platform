using OrderPlatform.Contracts;
using OrderPlatform.Messaging.Outbox;
using OrderService.Domain;

namespace OrderService.Messaging;

/// <summary>Order servisinin yayınladığı olayları tek yerde oluşturur.</summary>
public static class OrderOutboxEvents
{
    // correlationId = orderId: bir siparişin bütün servislerdeki olayları aynı kimlikle izlenebilir.

    public static OutboxMessage Created(Order order) =>
        OutboxMessage.From(order.Id, EventEnvelope.Create(
            EventTypes.OrderCreated, order.Id.ToString(),
            new OrderCreated(order.Id, order.CustomerId, order.TotalAmount, Lines(order))));

    public static OutboxMessage PaymentRequested(Order order) =>
        OutboxMessage.From(order.Id, EventEnvelope.Create(
            EventTypes.PaymentRequested, order.Id.ToString(),
            new PaymentRequested(order.Id, order.CustomerId, order.TotalAmount)));

    public static OutboxMessage Confirmed(Order order) =>
        OutboxMessage.From(order.Id, EventEnvelope.Create(
            EventTypes.OrderConfirmed, order.Id.ToString(),
            new OrderConfirmed(order.Id, order.CustomerId, order.TotalAmount, Lines(order))));

    public static OutboxMessage Cancelled(Order order) =>
        OutboxMessage.From(order.Id, EventEnvelope.Create(
            EventTypes.OrderCancelled, order.Id.ToString(),
            new OrderCancelled(order.Id, order.CustomerId, order.CancellationReason ?? "Cancelled")));

    private static List<OrderLine> Lines(Order order) =>
        order.Items.Select(i => new OrderLine(i.ProductId, i.Quantity, i.UnitPrice)).ToList();
}
