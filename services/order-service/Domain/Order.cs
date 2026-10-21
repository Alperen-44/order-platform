namespace OrderService.Domain;

/// <summary>Sipariş kalemi: yalnızca ürün ve adet. Fiyatı istemci değil Inventory belirler.</summary>
public sealed record OrderLineInput(Guid ProductId, int Quantity);

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

    /// <summary>Stok ayrılana kadar 0; MarkStockReserved Inventory'nin fiyatlarıyla hesaplar.</summary>
    public decimal TotalAmount { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items;

    /// <summary>
    /// Optimistic concurrency için PostgreSQL'in xmin sistem kolonu. Satır her güncellendiğinde değişir.
    /// Zaman aşımı servisi ile ödeme consumer'ı aynı siparişi aynı anda değiştirmeye kalkarsa
    /// ikincisi DbUpdateConcurrencyException alır ve kimsenin değişikliği sessizce ezilmez.
    /// </summary>
    public uint Version { get; private set; }

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
            order._items.Add(new OrderItem(order.Id, line.ProductId, line.Quantity));
        }

        if (order._items.Count == 0)
            throw new InvalidOperationException("Sipariş en az bir kalem içermelidir.");

        return order;
    }

    // ---------- Saga durum geçişleri ----------
    // Her metot geçişin yapılıp yapılmadığını döner. Sırası karışmış ya da geç gelmiş bir olay
    // (ör. zaman aşımından sonra gelen PaymentCompleted) false döner ve durumu değiştiremez.

    /// <summary>
    /// Pending → StockReserved (sırada ödeme var). Inventory'nin stok ayırırken bildirdiği birim
    /// fiyatlar kalemlere yazılır ve tutar bu fiyatlarla hesaplanır. Ödeme bu tutarla istenir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Bir kalemin fiyatı gelmediyse: olay sözleşmesi bozuk.</exception>
    public bool MarkStockReserved(IReadOnlyDictionary<Guid, decimal> unitPrices)
    {
        if (Status != OrderStatus.Pending) return false;

        // Önce hepsini doğrula, sonra yaz: yarım fiyatlanmış sipariş kalmasın.
        foreach (var item in _items)
        {
            if (!unitPrices.TryGetValue(item.ProductId, out var price))
                throw new InvalidOperationException($"No price received for product {item.ProductId}.");
            if (price < 0)
                throw new InvalidOperationException($"Negative price received for product {item.ProductId}.");
        }

        foreach (var item in _items)
            item.SetUnitPrice(unitPrices[item.ProductId]);

        TotalAmount = _items.Sum(i => i.Quantity * i.UnitPrice);
        Status = OrderStatus.StockReserved;
        UpdatedAt = DateTimeOffset.UtcNow; // zaman aşımı sayacı buradan başlar
        return true;
    }

    /// <summary>StockReserved → Confirmed</summary>
    public bool Confirm()
    {
        if (Status != OrderStatus.StockReserved) return false;

        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>Pending / StockReserved → Cancelled</summary>
    public bool Cancel(string reason)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.StockReserved))
            return false;

        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
