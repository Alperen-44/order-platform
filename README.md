# Order Platform: Olay Güdümlü Sipariş ve Stok Sistemi

Sipariş, stok, ödeme ve bildirim servislerinin Kafka üzerinden mesajlaşarak çalıştığı,
**hata, tekrar ve yarıda kalma durumlarında tutarlı kalan** bir backend sistemi.

> **Durum:** Faz 2 tamamlandı. Servisler Kafka üzerinden mesajlaşıyor: sipariş verildiğinde stok
> otomatik rezerve ediliyor, sonuç olay olarak Order'a dönüyor. Sırada ödeme adımı (Faz 4) var.

## Mimari (hedef)

```
 İstemci ──► API Gateway ──► Order Service (+ Saga) ──► Postgres (orders + outbox)
                                    │
                          ══════════▼═══════ KAFKA ═══════════════
                              ▲              ▲              ▲
                     Inventory Service  Payment Service  Notification
                     (Postgres)         (Postgres)       Service
```

## Bugünkü akış (Faz 2)

```
POST /orders
   │
   ▼
Order Service ── tek transaction ──► orders + outbox (OrderCreated)
                                          │  OutboxRelay (500 ms, SKIP LOCKED)
                                          ▼
                                   Kafka: order.events  (anahtar = orderId)
                                          │
                                          ▼
Inventory Service ◄── OrderCreatedConsumer
   │  atomik rezervasyon + outbox (StockReserved | StockReservationFailed) tek transaction
   ▼  OutboxRelay
Kafka: inventory.events
   │
   ▼
Order Service ◄── InventoryEventsConsumer
   Pending → StockReserved   ya da   Pending → Cancelled
```

## Teknolojiler
.NET 8 · ASP.NET Core Minimal API · EF Core 8 · PostgreSQL 16 · Apache Kafka 3.8 (KRaft) · Docker Compose

## Hızlı başlangıç

Gereksinimler: Docker Desktop, .NET 8 SDK (servisleri Docker dışında çalıştırmak istersen).

```bash
docker compose up --build -d      # her şeyi ayağa kaldır
./scripts/smoke-test.sh           # uçtan uca olay akışı
./scripts/race-test.sh 100        # "son ürün" eşzamanlılık testi
./scripts/kafka-outage-test.sh    # Kafka kapalıyken sipariş kaybolmuyor mu?
```

| Adres | Ne var |
|---|---|
| http://localhost:5001/swagger | Order Service |
| http://localhost:5002/swagger | Inventory Service |
| http://localhost:8080 | Kafka UI |

## Proje yapısı

```
order-platform/
├── services/
│   ├── order-service/         # sipariş, saga durumları, inventory olaylarını dinler
│   └── inventory-service/     # stok, rezervasyon, OrderCreated'ı dinler
├── shared/
│   ├── OrderPlatform.Contracts/   # olay sözleşmeleri (EventEnvelope, OrderCreated, StockReserved...)
│   └── OrderPlatform.Messaging/   # Kafka producer, OutboxRelay, KafkaConsumerService
├── scripts/                   # smoke, race ve Kafka kesinti testleri
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
- **Retry:** Başarısız mesaj artan beklemeyle (400 ms → 3,2 sn) 5 kez deneniyor.
- **Idempotent producer** (`EnableIdempotence`, `Acks.All`): Ağ hatasında yeniden gönderim Kafka'da kopya üretmiyor.
- **Durum makinesi = doğal idempotency:** `MarkStockReserved()` yalnızca `Pending` durumunda çalışıyor. Aynı olay iki kez gelirse ikinci seferde hiçbir şey değişmiyor.

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
- [ ] **Faz 3:** Idempotent consumer, retry ve DLQ
- [ ] **Faz 4:** Payment servisi, telafi işlemleri, zaman aşımı; Notification servisi
- [ ] **Faz 5:** API Gateway (YARP), Keycloak, Idempotency-Key başlığı
- [ ] **Faz 6:** Testcontainers ile entegrasyon testleri, GitHub Actions CI
- [ ] **Faz 7:** OpenTelemetry + Jaeger, Prometheus + Grafana
- [ ] **Faz 8:** k6 yük testleri, hata senaryosu demoları

## Bilinen sınırlamalar
- Fiyat istemciden geliyor; gerçek sistemde katalog servisinden alınmalı.
- 5 denemede işlenemeyen mesaj loglanıp atlanıyor. Faz 3'te Dead Letter Queue'ya gidecek.
- Tüketicilerde genel bir `processed_events` tablosu yok. Idempotency şimdilik iş kurallarına dayanıyor
  (rezervasyonda `orderId` kontrolü, Order'da durum makinesi). Faz 3'te eklenecek.
- Rezervasyonların süre aşımı (TTL) yok.
- Şema SQL init script'iyle oluşturuluyor (bkz. ADR 0002).
