# Order Platform: Olay Güdümlü Sipariş ve Stok Sistemi

Sipariş, stok, ödeme ve bildirim servislerinin Kafka üzerinden mesajlaşarak çalıştığı,
**hata, tekrar ve yarıda kalma durumlarında tutarlı kalan** bir backend sistemi.

> **Durum:** Faz 1 tamamlandı (Order + Inventory servisleri, ayrı veritabanları, outbox tablosu,
> atomik stok rezervasyonu). Kafka entegrasyonu ve Saga Faz 2'de.

## Mimari (hedef)

```
 İstemci ──► API Gateway ──► Order Service (+ Saga) ──► Postgres (orders + outbox)
                                    │
                          ══════════▼═══════ KAFKA ═══════════════
                              ▲              ▲              ▲
                     Inventory Service  Payment Service  Notification
                     (Postgres)         (Postgres)       Service
```

## Teknolojiler
.NET 8 · ASP.NET Core Minimal API · EF Core 8 · PostgreSQL 16 · Apache Kafka 3.8 (KRaft) · Docker Compose

## Hızlı başlangıç

Gereksinimler: Docker Desktop, .NET 8 SDK (servisleri Docker dışında çalıştırmak istersen).

```bash
docker compose up --build -d      # her şeyi ayağa kaldır
./scripts/smoke-test.sh           # temel akışı dene
./scripts/race-test.sh 100        # "son ürün" eşzamanlılık testi
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
│   ├── order-service/         # sipariş + outbox
│   └── inventory-service/     # stok + rezervasyon
├── shared/OrderPlatform.Contracts/   # olay sözleşmeleri (EventEnvelope, OrderCreated)
├── scripts/                   # smoke ve race testleri
├── docs/adr/                  # mimari karar kayıtları
└── docker-compose.yml
```

## Tasarım kararları ve çözülen problemler

### 1. Outbox pattern (Order Service)
Sipariş ile `OrderCreated` olayı **tek bir transaction** içinde yazılıyor
(`orders` + `outbox` tabloları). Böylece "sipariş kaydedildi ama olay kayboldu" durumu
oluşamıyor. Faz 2'de bir relay bu tablodan okuyup Kafka'ya gönderecek.

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

Detaylar: [`docs/adr`](docs/adr)

## Yol haritası

- [x] **Faz 1:** Order + Inventory servisleri, ayrı DB'ler, outbox tablosu, atomik rezervasyon
- [ ] **Faz 2:** Kafka entegrasyonu, outbox relay, saga'nın mutlu yolu
- [ ] **Faz 3:** Idempotent consumer, retry ve DLQ
- [ ] **Faz 4:** Payment servisi, telafi işlemleri, zaman aşımı; Notification servisi
- [ ] **Faz 5:** API Gateway (YARP), Keycloak, Idempotency-Key başlığı
- [ ] **Faz 6:** Testcontainers ile entegrasyon testleri, GitHub Actions CI
- [ ] **Faz 7:** OpenTelemetry + Jaeger, Prometheus + Grafana
- [ ] **Faz 8:** k6 yük testleri, hata senaryosu demoları

## Bilinen sınırlamalar (Faz 1)
- Fiyat istemciden geliyor; gerçek sistemde katalog servisinden alınmalı.
- Rezervasyon henüz REST ile tetikleniyor; Faz 2'de `OrderCreated` olayıyla tetiklenecek.
- Rezervasyonların süre aşımı (TTL) yok.
- Şema SQL init script'iyle oluşturuluyor (bkz. ADR 0002).
