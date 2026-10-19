using Microsoft.Extensions.Options;

namespace PaymentService.Provider;

public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProvider";

    /// <summary>0.0 – 1.0 arası rastgele hata oranı. 0.2 = ödemelerin %20'si reddedilir.</summary>
    public double FailureRate { get; set; }

    /// <summary>Normal bir ödemenin süresi (ağ gecikmesi gibi).</summary>
    public int DelayMs { get; set; } = 300;

    /// <summary>"Yavaş" test müşterisinin ödeme süresi. Saga zaman aşımını tetiklemek için.</summary>
    public int SlowDelayMs { get; set; } = 25_000;
}

/// <summary>
/// Testlerde sonucu önceden bilinen müşteriler. Gerçek ödeme sağlayıcılarının
/// "test kartları" gibi (ör. Stripe'ta reddedilen kart numarası).
/// </summary>
public static class TestCustomers
{
    /// <summary>Ödemesi her zaman reddedilir.</summary>
    public static readonly Guid AlwaysDeclined = Guid.Parse("00000000-0000-0000-0000-00000000fa11");

    /// <summary>Ödemesi SlowDelayMs kadar sürer (saga zaman aşımından uzun).</summary>
    public static readonly Guid Slow = Guid.Parse("00000000-0000-0000-0000-000000005103");
}

public sealed record ChargeResult(bool Success, string? Reference, string? FailureReason);

/// <summary>
/// Gerçek bir ödeme sağlayıcısının (iyzico, Stripe...) yerine geçen sahte sağlayıcı.
/// Hata oranı ve gecikme ayarlanabildiği için saga'nın hata yollarını denemeyi sağlar.
/// </summary>
public sealed class FakePaymentProvider(IOptions<PaymentProviderOptions> options, ILogger<FakePaymentProvider> logger)
{
    /// <param name="idempotencyKey">
    /// Gerçek sağlayıcılar aynı anahtarla gelen ikinci isteği yeniden çekim yapmadan cevaplar.
    /// Biz orderId gönderiyoruz: consumer tekrar denese bile müşteriden iki kez para çekilmez.
    /// </param>
    public async Task<ChargeResult> ChargeAsync(
        string idempotencyKey, Guid customerId, decimal amount, CancellationToken ct)
    {
        var settings = options.Value;
        var delay = customerId == TestCustomers.Slow ? settings.SlowDelayMs : settings.DelayMs;

        logger.LogInformation("Charging {Amount} for {IdempotencyKey} (simulated delay {Delay} ms)",
            amount, idempotencyKey, delay);
        await Task.Delay(delay, ct);

        if (customerId == TestCustomers.AlwaysDeclined)
            return new ChargeResult(false, null, "Card declined (test customer)");

        if (Random.Shared.NextDouble() < settings.FailureRate)
            return new ChargeResult(false, null, "Card declined (random failure)");

        var reference = ("pay_" + Guid.NewGuid().ToString("N"))[..20];
        return new ChargeResult(true, reference, null);
    }
}
