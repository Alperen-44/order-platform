namespace OrderPlatform.Contracts;

public static class Topics
{
    /// <summary>Order servisinin yayınladığı olaylar (anahtar: orderId).</summary>
    public const string OrderEvents = "order.events";

    /// <summary>Inventory servisinin yayınladığı olaylar (anahtar: orderId).</summary>
    public const string InventoryEvents = "inventory.events";

    /// <summary>Payment servisinin yayınladığı olaylar (anahtar: orderId).</summary>
    public const string PaymentEvents = "payment.events";
}

public static class EventTypes
{
    // Order
    public const string OrderCreated = "OrderCreated";
    public const string PaymentRequested = "PaymentRequested";
    public const string OrderConfirmed = "OrderConfirmed";
    public const string OrderCancelled = "OrderCancelled";

    // Inventory
    public const string StockReserved = "StockReserved";
    public const string StockReservationFailed = "StockReservationFailed";

    // Payment
    public const string PaymentCompleted = "PaymentCompleted";
    public const string PaymentFailed = "PaymentFailed";
    public const string PaymentRefunded = "PaymentRefunded";
}

// ---------- Order olayları ----------

public sealed record OrderLine(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderCreated(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderLine> Items);

/// <summary>Stok ayrıldı, sıra ödemede. Payment servisi bu olayla ödemeyi alır.</summary>
public sealed record PaymentRequested(Guid OrderId, Guid CustomerId, decimal Amount);

/// <summary>Saga başarıyla bitti. Inventory rezervasyonu kesinleştirir, Notification bildirir.</summary>
public sealed record OrderConfirmed(
    Guid OrderId,
    Guid CustomerId,
    decimal TotalAmount,
    IReadOnlyList<OrderLine> Items);

/// <summary>
/// Sipariş iptal edildi. Telafi işlemleri bu olayla tetiklenir:
/// Inventory stoğu geri bırakır, Payment alınmış bir ödeme varsa iade eder.
/// </summary>
public sealed record OrderCancelled(Guid OrderId, Guid CustomerId, string Reason);

// ---------- Inventory olayları ----------

public sealed record ReservedLine(Guid ProductId, int Quantity);

public sealed record StockReserved(Guid OrderId, IReadOnlyList<ReservedLine> Items);

public sealed record StockReservationFailed(Guid OrderId, string Reason);

// ---------- Payment olayları ----------

public sealed record PaymentCompleted(Guid OrderId, Guid PaymentId, decimal Amount);

public sealed record PaymentFailed(Guid OrderId, string Reason);

public sealed record PaymentRefunded(Guid OrderId, Guid CustomerId, Guid PaymentId, decimal Amount);
