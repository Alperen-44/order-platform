namespace OrderPlatform.Contracts;

public static class Topics
{
    public const string OrderEvents = "order.events";
}

public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderConfirmed = "OrderConfirmed";
    public const string OrderCancelled = "OrderCancelled";
}

public sealed record OrderLine(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderLine> Items);
