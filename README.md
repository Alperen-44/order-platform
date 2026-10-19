# Order Platform: Olay Güdümlü Sipariş ve Stok Sistemi

[![CI](https://github.com/Alperen-44/order-platform/actions/workflows/ci.yml/badge.svg)](https://github.com/Alperen-44/order-platform/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Kafka](https://img.shields.io/badge/Apache%20Kafka-3.8-231F20)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1)

Sipariş, stok, ödeme ve bildirim servislerinin Kafka üzerinden mesajlaşarak çalıştığı,
**hata, tekrar ve yarıda kalma durumlarında tutarlı kalan** bir backend sistemi.

> **Durum:** Faz 4 tamamlandı. Dört servis (Order, Inventory, Payment, Notification) Kafka üzerinden
> bir saga ile çalışıyor. Ödeme reddedilirse ya da zamanında gelmezse stok geri bırakılıyor,
> geç alınan ödeme iade ediliyor. Her olay bir kez işleniyor, işlenemeyenler DLQ'ya gidiyor.

## Mimari

```
                      POST /orders
                           │
                ┌──────────▼──────────┐
                │    Order Service    │  saga orkestratörü (durum makinesi + zaman aşımı)
                │  Postgres + outbox  │
                └──────────┬──────────┘
                           │ order.events
   ════════════════════════▼═══════ KAFKA ════════════════════════════════
        ▲ inventory.events           ▲ payment.events
        │                            │
┌───────┴──────────┐      ┌──────────┴───────┐      ┌──────────────────────┐
│ Inventory Service│      │ Payment Service  │      │ Notification Service │
│ stok rezervasyonu│      │ sahte sağlayıcı  │      │ (order + payment     │
│ Postgres+outbox  │      │ Postgres+outbox  │      │  olaylarını dinler)  │
└──────────────────┘      └──────────────────┘      └──────────────────────┘
```

Her servisin kendi veritabanı var; servisler birbirinin veritabanına hiç dokunmuyor.

## Saga akışı

**Mutlu yol:**
```
OrderCreated ──► Inventory: stok rezerve ──► StockReserved
            ──► Order: StockReserved, PaymentRequested ──► Payment: ödeme alındı ──► PaymentCompleted
            ──► Order: Confirmed, OrderConfirmed ──► Inventory: rezervasyon kesinleşti
                                                 └─► Notification: "Siparişiniz onaylandı"
```

**Hata yolları ve telafi işlemleri:**

| Ne oldu? | Sipariş | Telafi |
|---|---|---|
| Stok yok | `Cancelled` | Gerekmez (hiçbir şey ayrılmadı) |
| Ödeme reddedildi | `Cancelled` | Inventory stoğu geri bırakır |
| Ödeme 15 sn'de sonuçlanmadı | `Cancelled` (zaman aşımı) | Inventory stoğu geri bırakır |
| Ödeme zaman aşımından **sonra** alındı | `Cancelled` kalır | Payment parayı **iade eder** |

Durum makinesi:
```
Pending ──StockReserved──► StockReserved ──PaymentCompleted──► Confirmed
   │                           │
   │ StockReservationFailed    │ PaymentFailed / zaman aşımı
   ▼                           ▼
Cancelled ◄────────────────────┘
```

## Teknolojiler
.NET 8 · ASP.NET Core Minimal API · EF Core 8 · PostgreSQL 16 · Apache Kafka 3.8 (KRaft) · Docker Compose

## Hızlı başlangıç

Gereksinimler: Docker Desktop, .NET 8 SDK (servisleri Docker dışında çalıştırmak istersen).

```bash
docker compose up --build -d      # her şeyi ayağa kaldır
./scripts/smoke-test.sh           # saga'nın mutlu yolu + stok yetersizliği
./scripts/payment-failure-test.sh # ödeme reddedilince stok geri bırakılıyor mu?
./scripts/payment-timeout-test.sh # zaman aşımı + geç alınan ödemenin iadesi
./scripts/race-test.sh 100        # "son ürün" eşzamanlılık testi
./scripts/kafka-outage-test.sh    # Kafka kapalıyken sipariş kaybolmuyor mu?
./scripts/duplicate-event-test.sh # aynı olay 3 kez gelirse bir kez mi işleniyor?
./scripts/poison-message-test.sh  # bozuk mesaj DLQ'ya gidiyor, sistem kilitlenmiyor mu?
```

| Adres | Ne var |
|---|---|
| http://localhost:5001/swagger | Order Service |
| http://localhost:5002/swagger | Inventory Service |
| http://localhost:5003/swagger | Payment Service |
| http://localhost:5004/notifications | Notification Service (gönderilen bildirimler) |
| http://localhost:8080 | Kafka UI |

## Otomatik testler

```bash
dotnet test                                        # hepsi (entegrasyon testleri için Docker açık olmalı)
dotnet test --filter "FullyQualifiedName~UnitTests" # yalnızca birim testleri, Docker gerekmez
```

### Birim testleri
Docker gerekmez, birkaç saniyede biter. Test edilen kurallar:
- **Order durum makinesi:** geçerli geçişler; geçersiz geçişlerin reddedilmesi (iptal edilmiş sipariş
  sonradan onaylanamaz, onaylanmış sipariş iptal edilemez); aynı geçişin iki kez uygulanamaması.
- **Sipariş isteği doğrulaması:** boş müşteri, kalemsiz sipariş, geçersiz adet/fiyat, tekrarlanan ürün.
- **Payment:** yalnızca tamamlanmış ödeme iade edilebilir, iade yalnızca bir kez yapılır.
- **Sahte ödeme sağlayıcısı:** test müşterileri, hata oranı, iptal sinyali.

### Sürekli entegrasyon (GitHub Actions)
Her push ve pull request'te [`.github/workflows/ci.yml`](.github/workflows/ci.yml) çalışır:
derleme → birim testleri → entegrasyon testleri (GitHub'ın sunucusunda gerçek PostgreSQL) →
dört servisin Docker imajı → `docker-compose.yml` doğrulaması. Test sonuçları her çalışmada
indirilebilir dosya olarak saklanır.

### Entegrasyon testleri (Testcontainers)
Her test çalıştırmasında Docker'da geçici bir PostgreSQL açılır, servisin gerçek `init.sql` şeması kurulur,
testler bitince container silinir. Veritabanı seviyesindeki garantiler burada kanıtlanıyor:
- **Yarış durumu:** stokta 1 ürün varken 50 eşzamanlı rezervasyondan yalnızca 1'i başarılı olur.
- **Eşzamanlı tekrar:** aynı sipariş için aynı anda gelen 2 istek stoğu bir kez düşürür (`UNIQUE` + `SAVEPOINT`).
- **Ya hep ya hiç:** çok kalemli siparişte bir kalem yetersizse hiçbir kalem düşmez.
- **Telafi ve kesinleştirme:** iki kez release stoğu bir kez geri getirir; kesinleşmiş satış geri alınamaz.
- **Outbox:** sipariş ile olay birlikte yazılır; olay yazılamazsa sipariş de yazılmaz.
- **Optimistic concurrency:** aynı siparişi eşzamanlı değiştiren ikinci işlem `DbUpdateConcurrencyException` alır.

## Proje yapısı

```
order-platform/
├── services/
│   ├── order-service/         # sipariş, saga orkestrasyonu, zaman aşımı
│   ├── inventory-service/     # stok rezervasyonu, kesinleştirme, geri bırakma
│   ├── payment-service/       # sahte ödeme sağlayıcısı, iade
│   └── notification-service/  # müşteri bildirimleri
├── shared/
│   ├── OrderPlatform.Contracts/   # olay sözleşmeleri (EventEnvelope, OrderCreated, StockReserved...)
│   └── OrderPlatform.Messaging/   # Kafka producer, OutboxRelay, idempotent consumer + DLQ
├── tests/                     # birim + entegrasyon testleri (xUnit, FluentAssertions, Testcontainers)
├── scripts/                   # senaryo testleri (yarış, kesinti, tekrar, bozuk mesaj, ödeme hataları)
├── docs/adr/                  # mimari karar kayıtları
└── docker-compose.yml
```

## Tasarım kararları ve çözülen problemler

### 1. Outbox pattern (Order Service)
Sipariş ile `OrderCreated` olayı **tek bir transaction** içinde yazılıyor
(`orders` + `outbox` tabloları). Böylece "sipariş kaydedildi ama olay kayboldu" durumu
oluşamıyor. `OutboxRelay` bu tabloyu `FOR UPDATE SKIP LOCKED` ile okuyup Kafka'ya gönderiyor
(bkz. ADR 0003). Inventory de aynı yöntemle kendi olaylarını yayınlıyor.

### 2. Atomik stok rezervasyonu (Inventory Service)
Kontrol ve düşme tek bir SQL ifadesinde yapılıyor:
```sql
UPDATE stock SET available = available - @q, reserved = reserved + @q
WHERE product_id = @id AND available >= @q;   -- etkilenen satır 0 ise: yetersiz stok
```
"Önce oku, sonra yaz" yaklaşımında iki istek aynı son ürünü satabilir.
`race-test.sh` 100 eşzamanlı istekte yalnızca 1'inin başarılı olduğunu gösteriyor.

### 3. Idempotency
- Aynı `orderId` ile tekrar gelen rezervasyon isteği stoğu ikinci kez düşürmüyor, ilk sonucu dönüyor.
- Aynı anda gelen kopyaları `UNIQUE(order_id, product_id)` kısıtı yakalıyor.
- `release` (telafi işlemi) koşullu güncelleme sayesinde birden fazla çağrıldığında stoğu bir kez geri ekliyor.

### 4. Deadlock önleme
Çok kalemli rezervasyonlarda ürünler her zaman `ProductId` sırasıyla kilitleniyor.

### 5. Kafka tarafı
- **Mesaj anahtarı = `orderId`:** Aynı siparişin olayları aynı partition'a düşer ve sırası korunur.
- **Manuel offset commit:** Offset, mesaj başarıyla işlendikten sonra commit ediliyor. İşlerken çökersek mesaj tekrar gelir, kaybolmaz.
- **Retry ve DLQ:** Geçici hatalar artan beklemeyle (400 ms → 3,2 sn) 5 kez deneniyor, sonra consumer'ın
  kendi Dead Letter Queue'suna (`inventory-service.dlq`) gidiyor. Bozuk JSON gibi zehirli mesajlar
  tekrar denenmeden doğrudan DLQ'ya gidiyor. DLQ'ya yazılamazsa offset commit edilmiyor.
- **Idempotent producer** (`EnableIdempotence`, `Acks.All`): Ağ hatasında yeniden gönderim Kafka'da kopya üretmiyor.
- **Durum makinesi:** `MarkStockReserved()` yalnızca `Pending` durumunda çalışıyor; sırası karışan olaylar durumu geri alamıyor.

### 6. Idempotent consumer (`processed_events`)
Her tüketici işlediği `eventId` değerlerini, iş mantığının değişikliğiyle **aynı transaction'da**
`processed_events` tablosuna yazıyor. Aynı olay tekrar gelirse atlanıyor. Stok rezervasyonu kendi
transaction'ını açmak yerine bu transaction'a katılıyor ve kısmi geri alma için `SAVEPOINT` kullanıyor
(bkz. ADR 0004).

### 7. Saga: orkestrasyon, telafi, zaman aşımı
Akışı Order'daki durum makinesi yönetiyor; her geçiş bir sonraki adımı başlatan olayı outbox'a yazıyor.
`OrderCancelled` olayı telafi işlemlerini tetikliyor: Inventory stoğu bırakıyor, Payment alınmış ödemeyi
iade ediyor. `PaymentTimeoutWatcher` 15 sn'den uzun ödeme bekleyen siparişleri iptal ediyor. Zaman aşımı
servisi ile consumer'ın aynı siparişi aynı anda değiştirmesine karşı PostgreSQL `xmin` kolonu
optimistic concurrency token olarak kullanılıyor (bkz. ADR 0005).

### 8. Test edilebilir ödeme sağlayıcısı
Gerçek sağlayıcıların test kartları gibi, sonucu belli test müşterileri var:
`00000000-0000-0000-0000-00000000fa11` her zaman reddedilir, `...000000005103` 25 sn bekletir.
Rastgele hata oranı `PaymentProvider__FailureRate` ile ayarlanabiliyor (ör. `0.2` → %20 red).

Detaylar: [`docs/adr`](docs/adr)

## Test sonuçları (Faz 1)

**Yarış testi** (`./scripts/race-test.sh 100`): Stokta 1 adet kalan ürün için 100 eşzamanlı rezervasyon isteği.

```
Sonuç (HTTP kodu -> adet):
   1 201
  99 409

Bitiş stoğu: available = 0, reserved = 1
BAŞARILI: 100 istekten yalnızca 1 tanesi ürünü aldı.
```

**Idempotency ve telafi** (`./scripts/smoke-test.sh`):

| Senaryo | Beklenen | Sonuç |
|---|---|---|
| Aynı rezervasyon isteği 2 kez | 1. `201`, 2. `200`, stok yalnızca bir kez düşer | ✅ 50 → 48 (46 değil) |
| Aynı release isteği 2 kez | Stok yalnızca bir kez geri gelir | ✅ 48 → 50 (52 değil) |
| Sipariş + `OrderCreated` olayı | Aynı transaction içinde yazılır | ✅ Outbox'ta `publishedAt: null` kaydı |

## Yol haritası

- [x] **Faz 1:** Order + Inventory servisleri, ayrı DB'ler, outbox tablosu, atomik rezervasyon
- [x] **Faz 2:** Kafka entegrasyonu, outbox relay, saga'nın ilk adımı (stok rezervasyonu)
- [x] **Faz 3:** Idempotent consumer, retry ve DLQ
- [x] **Faz 4:** Payment servisi, telafi işlemleri, zaman aşımı; Notification servisi
- [ ] **Faz 5:** Otomatik testler
  - [x] 5.1 Birim testleri (durum makinesi, doğrulama, ödeme kuralları)
  - [x] 5.2 Testcontainers ile PostgreSQL entegrasyon testleri
  - [ ] 5.3 Uçtan uca saga testi (Kafka + 4 PostgreSQL)
- [x] **Faz 6:** GitHub Actions CI ve build rozeti
- [ ] **Faz 7:** API Gateway (YARP), Keycloak + JWT, Idempotency-Key, rate limiting
- [ ] **Faz 8:** OpenTelemetry + Jaeger, Prometheus + Grafana
- [ ] **Faz 9:** k6 yük testleri
- [ ] **Faz 10:** Sunum: diyagramlar, demo, blog yazısı

## Bilinen sınırlamalar
- Fiyat istemciden geliyor; gerçek sistemde katalog servisinden alınmalı.
- DLQ'daki mesajları tekrar oynatmak için henüz bir araç yok (elle yapılıyor).
- `processed_events` tablosu temizlenmiyor; gerçek sistemde eski kayıtlar silinmeli.
- Ödeme sağlayıcısı consumer içinde senkron çağrılıyor; yavaş bir ödeme aynı partition'daki diğerlerini bekletiyor.
- Bildirimler simüle ediliyor (log + veritabanı); gerçek e-posta gönderilmiyor.
- Şema SQL init script'iyle oluşturuluyor (bkz. ADR 0002).
