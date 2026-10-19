namespace NotificationService.Domain;

/// <summary>Müşteriye gönderilen (bu projede simüle edilen) bir bildirim.</summary>
public class Notification
{
    private Notification() { } // EF Core için

    public Notification(Guid orderId, Guid customerId, string type, string message)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        CustomerId = customerId;
        Type = type;
        Message = message;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }

    /// <summary>Bildirimi tetikleyen olay: OrderConfirmed, OrderCancelled, PaymentRefunded.</summary>
    public string Type { get; private set; } = default!;

    public string Message { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
}
