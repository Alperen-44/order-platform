namespace InventoryService.Domain;

public class StockItem
{
    private StockItem() { } // EF Core için

    public StockItem(Guid productId, string sku, string name, int initialStock, decimal price = 0)
    {
        if (initialStock < 0) throw new ArgumentOutOfRangeException(nameof(initialStock));
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));

        ProductId = productId;
        Sku = sku;
        Name = name;
        Price = price;
        Available = initialStock;
        Reserved = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    /// <summary>Birim satış fiyatı (TL).</summary>
    public decimal Price { get; private set; }

    /// <summary>Satılabilir adet.</summary>
    public int Available { get; private set; }

    /// <summary>Bir siparişe ayrılmış ama henüz kesinleşmemiş adet.</summary>
    public int Reserved { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
