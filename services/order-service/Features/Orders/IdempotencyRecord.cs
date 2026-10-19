using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrderService.Features.Orders;

/// <summary>
/// "Bu müşteri bu Idempotency-Key ile şu siparişi oluşturdu" kaydı.
/// Kullanıcı "Sipariş ver" butonuna iki kez basarsa, mobil bağlantı kopup istek tekrar gönderilirse
/// ya da istemci zaman aşımında yeniden denerse ikinci istek yeni sipariş oluşturmaz.
/// </summary>
public class IdempotencyRecord
{
    private IdempotencyRecord() { } // EF Core için

    public IdempotencyRecord(Guid customerId, string key, string requestHash, Guid orderId)
    {
        CustomerId = customerId;
        Key = key;
        RequestHash = requestHash;
        OrderId = orderId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid CustomerId { get; private set; }
    public string Key { get; private set; } = default!;

    /// <summary>
    /// İsteğin gövdesinin parmak izi. Aynı anahtar FARKLI bir sepetle tekrar kullanılırsa
    /// bu bir istemci hatasıdır; ilk siparişi sessizce dönmek yerine 422 veririz.
    /// </summary>
    public string RequestHash { get; private set; } = default!;

    public Guid OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static string HashOf(CreateOrderRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
