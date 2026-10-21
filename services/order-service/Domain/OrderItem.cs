namespace OrderService.Domain;

public class OrderItem
{
    private OrderItem() { } // EF Core için

    internal OrderItem(Guid orderId, Guid productId, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    /// <summary>Stok ayrılınca Inventory'nin bildirdiği fiyat. O zamana kadar 0.</summary>
    public decimal UnitPrice { get; private set; }

    internal void SetUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        UnitPrice = unitPrice;
    }
}
