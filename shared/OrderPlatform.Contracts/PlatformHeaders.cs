namespace OrderPlatform.Contracts;

/// <summary>Gateway ile servisler arasında kullanılan HTTP başlıkları.</summary>
public static class PlatformHeaders
{
    /// <summary>
    /// Giriş yapmış müşterinin kimliği (Keycloak token'ındaki "sub").
    /// YALNIZCA gateway yazar: istemcinin gönderdiği aynı adlı başlık gateway'de silinir,
    /// yerine token'dan okunan değer konur. Böylece kimse başkası adına sipariş veremez.
    /// </summary>
    public const string CustomerId = "X-Customer-Id";

    /// <summary>
    /// İstemcinin her "Sipariş ver" denemesi için ürettiği benzersiz anahtar.
    /// Aynı anahtarla gelen ikinci istek yeni sipariş oluşturmaz, ilk siparişi döner.
    /// </summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>Cevabın önceki bir isteğin tekrarı olduğunu belirtir.</summary>
    public const string IdempotentReplayed = "Idempotent-Replayed";
}
