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
}
