# ADR 0004: Idempotent consumer (`processed_events`) ve consumer başına Dead Letter Queue

**Durum:** Kabul edildi · **Tarih:** 2026-10-05

## Bağlam
Kafka ve outbox relay "en az bir kez" teslim garantisi veriyor: aynı olay birden fazla kez gelebilir.
Faz 2'de tekrarlar iş kurallarıyla yakalanıyordu (rezervasyonda `orderId` kontrolü, Order'da durum
makinesi). Bu yaklaşım her yeni olay tipi için ayrıca düşünülmesi gereken, kolay unutulan bir kural.

Ayrıca işlenemeyen bir mesaj 5 denemeden sonra yalnızca loglanıp atlanıyordu, yani kayboluyordu.

## Karar

### 1. `processed_events` tablosu
Her servis, işlediği olayları `(consumer_group, event_id)` birincil anahtarıyla kaydediyor.
`KafkaConsumerService` her mesaj için:

1. Transaction açar
2. `processed_events`'te bu `eventId` varsa mesajı atlar
3. İş mantığını çalıştırır (aynı DbContext, aynı transaction)
4. `processed_events`'e kaydı ekler ve commit eder
5. Ardından Kafka offset'ini commit eder

İş mantığının kendi transaction'ı gerekiyorsa (stok rezervasyonu) dış transaction'a katılıyor ve
kısmi geri alma için `SAVEPOINT` kullanıyor.

### 2. Dead Letter Queue
- **Zehirli mesaj** (bozuk JSON, eksik `event-id` başlığı): tekrar denenmeden DLQ'ya gider.
- **Geçici hata** (veritabanı erişilemiyor vb.): artan beklemeyle 5 kez denenir, sonra DLQ'ya gider.
- DLQ consumer group başına ayrı: `inventory-service.dlq`, `order-service.dlq`. Aynı topic'i dinleyen
  iki servisten biri hata alırsa sorun o servise aittir.
- Orijinal mesaj aynen korunur. Başlıklara `dlq-error`, `dlq-original-topic/partition/offset`,
  `dlq-attempts` ve `dlq-failed-at` eklenir.
- DLQ'ya yazılamazsa offset commit edilmez. Mesaj hiçbir koşulda sessizce kaybolmaz.

## Sonuçlar
- (+) Idempotency her olay tipi için otomatik. Yeni bir consumer yazan geliştiricinin aklında tutması gerekmiyor.
- (+) Bir bozuk mesaj partition'ı kilitlemiyor; arkasındaki mesajlar işlenmeye devam ediyor.
- (+) Hatalı mesajlar incelenebiliyor ve düzeltildikten sonra tekrar oynatılabiliyor.
- (−) Her mesaj için fazladan bir okuma ve bir yazma.
- (−) `processed_events` sürekli büyüyor. Gerçek sistemde eski kayıtlar (örneğin 30 günden eski) temizlenmeli.
- (−) DLQ'dan tekrar oynatma şu an elle yapılıyor. Bir "replay" aracı ileride eklenebilir.
