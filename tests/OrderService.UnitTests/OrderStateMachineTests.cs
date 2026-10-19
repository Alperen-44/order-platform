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

    private static Order NewOrder() => Order.Create(Customer,
    [
        new OrderLineInput(Guid.NewGuid(), 2, 1500m),
        new OrderLineInput(Guid.NewGuid(), 1, 250.50m)
    ]);

    // ---------- Oluşturma ----------

    [Fact]
    public void Yeni_siparis_Pending_durumunda_baslar_ve_toplam_dogru_hesaplanir()
    {
        var order = NewOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.CustomerId.Should().Be(Customer);
        order.TotalAmount.Should().Be(3250.50m); // 2 x 1500 + 1 x 250.50
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
        var act = () => Order.Create(Customer, [new OrderLineInput(Guid.NewGuid(), quantity, 10m)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------- Mutlu yol ----------

    [Fact]
    public void Mutlu_yol_Pending_StockReserved_Confirmed()
    {
        var order = NewOrder();

        order.MarkStockReserved().Should().BeTrue();
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

        order.MarkStockReserved().Should().BeTrue();
        order.MarkStockReserved().Should().BeFalse();
        order.Status.Should().Be(OrderStatus.StockReserved);
    }

    [Fact]
    public void Iptal_edilmis_siparis_sonradan_onaylanamaz()
    {
        // Geç gelen ödeme senaryosu: zaman aşımıyla iptal, ardından PaymentCompleted geliyor.
        var order = NewOrder();
        order.MarkStockReserved();
        order.Cancel("Payment timeout");

        order.Confirm().Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Payment timeout");
    }

    [Fact]
    public void Onaylanmis_siparis_iptal_edilemez()
    {
        var order = NewOrder();
        order.MarkStockReserved();
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

        order.MarkStockReserved().Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ---------- İptal ----------

    [Theory]
    [InlineData(false)] // Pending durumundan (stok yok)
    [InlineData(true)]  // StockReserved durumundan (ödeme reddi / zaman aşımı)
    public void Siparis_Pending_ya_da_StockReserved_durumundayken_iptal_edilebilir(bool stockReserved)
    {
        var order = NewOrder();
        if (stockReserved) order.MarkStockReserved();

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
