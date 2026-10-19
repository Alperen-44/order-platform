using PaymentService.Domain;

namespace PaymentService.UnitTests;

/// <summary>
/// Ödeme kuralları. En kritik kural: yalnızca başarıyla alınmış bir ödeme iade edilebilir,
/// hem de yalnızca bir kez. Aksi halde müşteriye çekilmeyen para ödenir ya da iki kez iade yapılır.
/// </summary>
public class PaymentTests
{
    private static readonly Guid Order = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public void Basarili_odeme_Completed_durumunda_olusur()
    {
        var payment = Payment.Succeeded(Order, Customer, 1500m, "pay_123");

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.OrderId.Should().Be(Order);
        payment.Amount.Should().Be(1500m);
        payment.ProviderReference.Should().Be("pay_123");
        payment.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Reddedilen_odeme_Failed_durumunda_olusur_ve_sebebi_saklanir()
    {
        var payment = Payment.Declined(Order, Customer, 1500m, "Card declined");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Card declined");
        payment.ProviderReference.Should().BeNull();
    }

    [Fact]
    public void Tamamlanmis_odeme_iade_edilebilir()
    {
        var payment = Payment.Succeeded(Order, Customer, 1500m, "pay_123");

        payment.Refund().Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.UpdatedAt.Should().BeOnOrAfter(payment.CreatedAt);
    }

    [Fact]
    public void Ayni_odeme_ikinci_kez_iade_edilemez()
    {
        // OrderCancelled olayı iki kez gelirse müşteriye iki kez para iade edilmemeli.
        var payment = Payment.Succeeded(Order, Customer, 1500m, "pay_123");
        payment.Refund();

        payment.Refund().Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void Basarisiz_odeme_iade_edilemez()
    {
        // Para hiç çekilmedi; iade edilecek bir şey yok.
        var payment = Payment.Declined(Order, Customer, 1500m, "Card declined");

        payment.Refund().Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void Her_odemenin_kimligi_benzersizdir()
    {
        var first = Payment.Succeeded(Order, Customer, 10m, "a");
        var second = Payment.Succeeded(Guid.NewGuid(), Customer, 10m, "b");

        first.Id.Should().NotBe(second.Id);
    }
}
