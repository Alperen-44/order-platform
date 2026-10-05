namespace OrderService.Domain;

public sealed record OrderLineInput(Guid ProductId, int Quantity, decimal UnitPrice);

/// <summary>
/// Sipariş aggregate'i. Durum yalnızca bu sınıfın metotlarıyla değişir;
/// dışarıdan "order.Status = ..." yazılamaz.
/// </summary>
public class Order
{
    private readonly List<OrderItem> _items = new();

    private Order() { } // EF Core için

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items;

    public static Order Create(Guid customerId, IEnumerable<OrderLineInput> lines)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var line in lines)
        {
            order._items.Add(new OrderItem(order.Id, line.ProductId, line.Quantity, line.UnitPrice));
        }

        if (order._items.Count == 0)
            throw new InvalidOperationException("Sipariş en az bir kalem içermelidir.");

        order.TotalAmount = order._items.Sum(i => i.Quantity * i.UnitPrice);
        return order;
    }

    // ---------- Saga durum geçişleri ----------
    // Her metot geçişin yapılıp yapılmadığını döner. Aynı olay iki kez gelirse ikinci çağrı
    // false döner ve hiçbir şey değişmez: durum makinesi tüketiciyi doğal olarak idempotent yapıyor.

    /// <summary>Pending → StockReserved</summary>
    public bool MarkStockReserved()
    {
        if (Status != OrderStatus.Pending) return false;

        Status = OrderStatus.StockReserved;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Pending / StockReserved → Cancelled</summary>
    public bool Cancel(string reason)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.StockReserved or OrderStatus.Cancelling))
            return false;

        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
