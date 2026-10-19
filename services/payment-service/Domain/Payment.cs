namespace PaymentService.Domain;

public enum PaymentStatus
{
    Completed,
    Failed,
    Refunded
}

/// <summary>Bir sipariş için yapılan ödeme denemesi. Her siparişin en fazla bir ödemesi olur.</summary>
public class Payment
{
    private Payment() { } // EF Core için

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>Ödeme sağlayıcısının işlem numarası (başarılı ödemelerde).</summary>
    public string? ProviderReference { get; private set; }

    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Payment Succeeded(Guid orderId, Guid customerId, decimal amount, string providerReference) =>
        New(orderId, customerId, amount, PaymentStatus.Completed, providerReference, null);

    public static Payment Declined(Guid orderId, Guid customerId, decimal amount, string reason) =>
        New(orderId, customerId, amount, PaymentStatus.Failed, null, reason);

    /// <summary>Completed → Refunded. Başarısız ya da zaten iade edilmiş ödemede false döner.</summary>
    public bool Refund()
    {
        if (Status != PaymentStatus.Completed) return false;

        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    private static Payment New(
        Guid orderId, Guid customerId, decimal amount, PaymentStatus status, string? reference, string? reason)
    {
        var now = DateTimeOffset.UtcNow;
        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Status = status,
            ProviderReference = reference,
            FailureReason = reason,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
