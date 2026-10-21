using OrderService.Domain;

namespace OrderService.UnitTests;

/// <summary>
/// Saga'nın kalbi: Order durum makinesi.
///
///   Pending ──► StockReserved ──► Confirmed
///      │              │
///      └──────────────┴──► Cancelled
///
/// Bu testler geçersiz geçişlerin reddedildiğini garanti eder. Örneğin zaman aşımıyla iptal
/// edilmiş bir siparişe sonradan gelen PaymentCompleted, siparişi "Confirmed" yapamaz.
/// </summary>
public class OrderStateMachineTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Keyboard = Guid.NewGuid();
    private static readonly Guid Mouse = Guid.NewGuid();

    private static Order NewOrder() => Order.Create(Customer,
    [
        new OrderLineInput(Keyboard, 2),
        new OrderLineInput(Mouse, 1)
    ]);

    /// <summary>Inventory'nin StockReserved olayında bildirdiği fiyatlar.</summary>
    private static readonly Dictionary<Guid, decimal> Prices = new()
    {
        [Keyboard] = 1500m,
        [Mouse] = 250.50m
    };

    // ---------- Oluşturma ----------

    [Fact]
    public void Yeni_siparis_Pending_durumunda_ve_fiyatsiz_baslar()
    {
        var order = NewOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.CustomerId.Should().Be(Customer);
        order.TotalAmount.Should().Be(0m, "fiyatı istemci değil Inventory belirler, stok ayrılınca");
        order.Items.Should().OnlyContain(i => i.UnitPrice == 0m);
        order.Items.Should().HaveCount(2);
        order.Items.Should().OnlyContain(i => i.OrderId == order.Id);
        order.CancellationReason.Should().BeNull();
    }

    [Fact]
    public void Kalemsiz_siparis_olusturulamaz()
    {
        var act = () => Order.Create(Customer, []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Adedi_sifir_ya_da_negatif_kalem_olusturulamaz(int quantity)
    {
        var act = () => Order.Create(Customer, [new OrderLineInput(Guid.NewGuid(), quantity)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------- Fiyatlandırma ----------

    [Fact]
    public void Stok_ayrilinca_Inventory_fiyatlari_yazilir_ve_toplam_hesaplanir()
    {
        var order = NewOrder();

        order.MarkStockReserved(Prices).Should().BeTrue();

        order.Items.Single(i => i.ProductId == Keyboard).UnitPrice.Should().Be(1500m);
        order.Items.Single(i => i.ProductId == Mouse).UnitPrice.Should().Be(250.50m);
        order.TotalAmount.Should().Be(3250.50m); // 2 x 1500 + 1 x 250.50
    }

    [Fact]
    public void Bir_kalemin_fiyati_gelmezse_hata_verir_ve_siparis_degismez()
    {
        // Olay sözleşmesi bozuksa sessizce 0 TL'ye satmak yerine hata verilir; mesaj DLQ'ya düşer.
        var order = NewOrder();
        var missingMouse = new Dictionary<Guid, decimal> { [Keyboard] = 1500m };

        var act = () => order.MarkStockReserved(missingMouse);

        act.Should().Throw<InvalidOperationException>();
        order.Status.Should().Be(OrderStatus.Pending);
        order.TotalAmount.Should().Be(0m);
        order.Items.Should().OnlyContain(i => i.UnitPrice == 0m, "yarım fiyatlanmış sipariş kalmamalı");
    }

    [Fact]
    public void Tekrar_gelen_StockReserved_fiyatlari_degistiremez()
    {
        var order = NewOrder();
        order.MarkStockReserved(Prices);

        var cheaper = new Dictionary<Guid, decimal> { [Keyboard] = 1m, [Mouse] = 1m };
        order.MarkStockReserved(cheaper).Should().BeFalse();

        order.TotalAmount.Should().Be(3250.50m);
    }

    // ---------- Mutlu yol ----------

    [Fact]
    public void Mutlu_yol_Pending_StockReserved_Confirmed()
    {
        var order = NewOrder();

        order.MarkStockReserved(Prices).Should().BeTrue();
        order.Status.Should().Be(OrderStatus.StockReserved);

        order.Confirm().Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.UpdatedAt.Should().BeOnOrAfter(order.CreatedAt);
    }

    // ---------- Geçersiz geçişler ----------

    [Fact]
    public void Stok_ayrilmadan_siparis_onaylanamaz()
    {
        var order = NewOrder();

        order.Confirm().Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Ayni_gecis_iki_kez_denenirse_ikincisi_reddedilir()
    {
        // Kafka aynı StockReserved olayını iki kez teslim edebilir.
        var order = NewOrder();

        order.MarkStockReserved(Prices).Should().BeTrue();
        order.MarkStockReserved(Prices).Should().BeFalse();
        order.Status.Should().Be(OrderStatus.StockReserved);
    }

    [Fact]
    public void Iptal_edilmis_siparis_sonradan_onaylanamaz()
    {
        // Geç gelen ödeme senaryosu: zaman aşımıyla iptal, ardından PaymentCompleted geliyor.
        var order = NewOrder();
        order.MarkStockReserved(Prices);
        order.Cancel("Payment timeout");

        order.Confirm().Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Payment timeout");
    }

    [Fact]
    public void Onaylanmis_siparis_iptal_edilemez()
    {
        var order = NewOrder();
        order.MarkStockReserved(Prices);
        order.Confirm();

        order.Cancel("Too late").Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Confirmed);
        order.CancellationReason.Should().BeNull();
    }

    [Fact]
    public void Iptal_edilmis_siparis_tekrar_stok_ayrilmis_duruma_gecemez()
    {
        var order = NewOrder();
        order.Cancel("Out of stock");

        order.MarkStockReserved(Prices).Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ---------- İptal ----------

    [Theory]
    [InlineData(false)] // Pending durumundan (stok yok)
    [InlineData(true)]  // StockReserved durumundan (ödeme reddi / zaman aşımı)
    public void Siparis_Pending_ya_da_StockReserved_durumundayken_iptal_edilebilir(bool stockReserved)
    {
        var order = NewOrder();
        if (stockReserved) order.MarkStockReserved(Prices);

        order.Cancel("Card declined").Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Card declined");
    }

    [Fact]
    public void Ikinci_iptal_reddedilir_ve_ilk_sebep_korunur()
    {
        var order = NewOrder();
        order.Cancel("First reason");

        order.Cancel("Second reason").Should().BeFalse();
        order.CancellationReason.Should().Be("First reason");
    }

    [Fact]
    public void Cok_uzun_iptal_sebebi_500_karaktere_kirpilir()
    {
        // Veritabanında kolon VARCHAR(500); uzun bir hata mesajı kaydı patlatmamalı.
        var order = NewOrder();

        order.Cancel(new string('x', 800));

        order.CancellationReason.Should().HaveLength(500);
    }
}
