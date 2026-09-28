namespace InventoryService.Domain;

public class StockItem
{
    private StockItem() { } // EF Core için

    public StockItem(Guid productId, string sku, string name, int initialStock)
    {
        if (initialStock < 0) throw new ArgumentOutOfRangeException(nameof(initialStock));

        ProductId = productId;
        Sku = sku;
        Name = name;
        Available = initialStock;
        Reserved = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    /// <summary>Satılabilir adet.</summary>
    public int Available { get; private set; }

    /// <summary>Bir siparişe ayrılmış ama henüz kesinleşmemiş adet.</summary>
    public int Reserved { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
