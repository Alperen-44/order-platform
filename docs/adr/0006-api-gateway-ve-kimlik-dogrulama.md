# ADR 0006: API Gateway (YARP) ve Keycloak ile kimlik doğrulama

**Durum:** Kabul edildi · **Tarih:** 2026-10-19

## Bağlam
Faz 6'ya kadar her servis kendi portundan doğrudan erişilebiliyordu ve sipariş veren müşterinin kimliği
istek gövdesindeki `customerId` alanından alınıyordu. Bu, herkesin istediği müşteri adına sipariş
verebilmesi ve başkasının siparişini görebilmesi demekti.

## Karar

### 1. Tek giriş noktası: YARP
`services/api-gateway` dış dünyanın gördüğü tek adres (`:8000`). `/api/orders`, `/api/products`,
`/api/payments` ve `/api/notifications` istekleri ilgili servise yönlendiriliyor. Stok rezervasyonu ve
debug uç noktaları gateway'den erişilemiyor (iç kullanım).

YARP seçildi, çünkü .NET ekosisteminin içinde, yapılandırmayla çalışıyor ve gerektiğinde C# ile
genişletilebiliyor (kimlik aktarımı gibi).

### 2. Kimlik: Keycloak + JWT, doğrulama yalnızca gateway'de
- Kullanıcı Keycloak'tan token alır (frontend'de Authorization Code + PKCE, testlerde password grant).
- Gateway token'ın imzasını, `iss` ve `aud` alanlarını doğrular.
- Token'daki `sub` değeri `X-Customer-Id` başlığıyla servislere iletilir.
- **İstemcinin gönderdiği `X-Customer-Id` başlığı gateway'de silinir.** Aksi halde herkes başlığı
  elle yazarak başkası gibi davranabilirdi (`gateway-test.sh` 4. adım).
- Servisler token'ı tekrar doğrulamıyor, başlığa güveniyor. Bu güven, servislerin dışarıya kapalı
  olmasına dayanıyor. Gerçek kurulumda 5001-5004 portları açılmaz; burada yalnızca geliştirme ve
  test scriptleri için açık.

### 3. Sahiplik
Gateway'den gelen isteklerde servisler sorguları müşteriye göre filtreliyor. Başkasının siparişi,
ödemesi ya da bildirimi istendiğinde **404** dönüyor. 403 dönmek o kaydın var olduğunu ele verirdi.

### 4. Idempotency-Key
`POST /orders` isteğinde `Idempotency-Key` başlığı gönderilebiliyor:
- Anahtar ilk kez geliyorsa sipariş, outbox olayı ve `idempotency_keys` kaydı **tek transaction'da** yazılır.
- Aynı anahtar aynı içerikle tekrar gelirse yeni sipariş oluşturulmaz. İlk sipariş `200` ve
  `Idempotent-Replayed: true` başlığıyla döner.
- Aynı anahtar **farklı** içerikle gelirse `422` döner. Bu bir istemci hatasıdır ve sessizce eski
  siparişi dönmek onu gizlerdi.
- Aynı anahtarla iki istek aynı anda gelirse `(customer_id, key)` birincil anahtarı ikincisini durdurur
  ve kazananın siparişi döner.

Anahtar müşteri başına benzersiz; iki farklı müşteri aynı anahtarı kullanabilir.

### 5. Rate limiting
Gateway'de kullanıcı başına sabit pencere: 10 saniyede 20 istek (`appsettings.json → RateLimiting`).
Giriş yapmamış isteklerde IP adresine göre. Sınır aşılınca `429 Too Many Requests`.

## Sonuçlar
- (+) Kimse başkası adına sipariş veremiyor ya da başkasının verisini göremiyor.
- (+) Çift tıklama veya ağ hatası yüzünden çift sipariş oluşmuyor.
- (+) Frontend tek bir adresle ve standart OIDC akışıyla çalışabilecek.
- (−) Gateway tek hata noktası. Gerçek sistemde birden fazla kopya ve önünde load balancer olur.
- (−) Servisler arası iletişim kimlik doğrulamasız (başlığa güven). Daha sıkı bir model için
  servisler token'ı kendileri doğrulayabilir ya da mTLS kullanılabilir.
- (−) `idempotency_keys` tablosu temizlenmiyor. Gerçek sistemde 24 saat gibi bir süre sonra silinir.
