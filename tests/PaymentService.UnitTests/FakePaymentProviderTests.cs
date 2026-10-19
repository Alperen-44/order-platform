using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentService.Provider;

namespace PaymentService.UnitTests;

/// <summary>
/// Sahte ödeme sağlayıcısının test müşterileri ve hata oranı ayarı.
/// Saga testleri bu davranışlara güveniyor, o yüzden onları da test ediyoruz.
/// </summary>
public class FakePaymentProviderTests
{
    private static FakePaymentProvider Provider(double failureRate = 0) =>
        new(Options.Create(new PaymentProviderOptions { FailureRate = failureRate, DelayMs = 0, SlowDelayMs = 0 }),
            NullLogger<FakePaymentProvider>.Instance);

    [Fact]
    public async Task Normal_musterinin_odemesi_basarili_olur_ve_islem_numarasi_doner()
    {
        var result = await Provider().ChargeAsync("order-1", Guid.NewGuid(), 100m, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Reference.Should().StartWith("pay_").And.HaveLength(20);
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Reddedilen_test_musterisinin_odemesi_her_zaman_basarisiz_olur()
    {
        var result = await Provider().ChargeAsync("order-1", TestCustomers.AlwaysDeclined, 100m, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Reference.Should().BeNull();
        result.FailureReason.Should().Contain("declined");
    }

    [Fact]
    public async Task Hata_orani_yuzde_yuz_ise_her_odeme_reddedilir()
    {
        var provider = Provider(failureRate: 1.0);

        for (var i = 0; i < 20; i++)
        {
            var result = await provider.ChargeAsync($"order-{i}", Guid.NewGuid(), 100m, CancellationToken.None);
            result.Success.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Hata_orani_sifir_ise_hicbir_odeme_reddedilmez()
    {
        var provider = Provider(failureRate: 0);

        for (var i = 0; i < 20; i++)
        {
            var result = await provider.ChargeAsync($"order-{i}", Guid.NewGuid(), 100m, CancellationToken.None);
            result.Success.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Iptal_sinyali_gelirse_odeme_beklemeyi_birakir()
    {
        var provider = new FakePaymentProvider(
            Options.Create(new PaymentProviderOptions { DelayMs = 10_000 }),
            NullLogger<FakePaymentProvider>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => provider.ChargeAsync("order-1", Guid.NewGuid(), 100m, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
