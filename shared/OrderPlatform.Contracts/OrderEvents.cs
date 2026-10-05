namespace OrderPlatform.Contracts;

public static class Topics
{
    /// <summary>Order servisinin yayınladığı olaylar (anahtar: orderId).</summary>
    public const string OrderEvents = "order.events";

    /// <summary>Inventory servisinin yayınladığı olaylar (anahtar: orderId).</summary>
    public const string InventoryEvents = "inventory.events";
}

public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderConfirmed = "OrderConfirmed";
    public const string OrderCancelled = "OrderCancelled";

    public const string StockReserved = "StockReserved";
    public const string StockReservationFailed = "StockReservationFailed";
}

// ---------- Order olayları ----------

public sealed record OrderLine(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderLine> Items);

// ---------- Inventory olayları ----------

public sealed record ReservedLine(Guid ProductId, int Quantity);

public sealed record StockReserved(Guid OrderId, IReadOnlyList<ReservedLine> Items);

public sealed record StockReservationFailed(Guid OrderId, string Reason);
