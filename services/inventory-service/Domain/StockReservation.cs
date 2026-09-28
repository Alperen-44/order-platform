namespace InventoryService.Domain;

public enum ReservationStatus
{
    Reserved,
    Released,
    Committed
}

public class StockReservation
{
    private StockReservation() { } // EF Core için

    public StockReservation(Guid orderId, Guid productId, int quantity)
    {
        var now = DateTimeOffset.UtcNow;
        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        Status = ReservationStatus.Reserved;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
