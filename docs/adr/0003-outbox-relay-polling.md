# ADR 0003: Outbox relay, polling + `FOR UPDATE SKIP LOCKED` ile çalışıyor

**Durum:** Kabul edildi · **Tarih:** 2026-10-05

## Bağlam
Outbox tablosundaki olayların Kafka'ya taşınması gerekiyor. İki yaygın yol var:

1. **Polling:** Uygulama içindeki bir arka plan servisi tabloyu belirli aralıklarla okur.
2. **CDC (Change Data Capture):** Debezium, PostgreSQL'in WAL kayıtlarını okuyup Kafka'ya yazar.

## Karar
Polling ile başlıyoruz. Relay her 500 ms'de bir:

```sql
SELECT * FROM outbox
WHERE published_at IS NULL
ORDER BY created_at
LIMIT 100
FOR UPDATE SKIP LOCKED;
```

sorgusunu çalıştırıyor, olayları `orderId` anahtarıyla Kafka'ya gönderiyor ve `published_at`
alanını aynı transaction içinde dolduruyor.

## Sonuçlar
- (+) Ek altyapı yok. Kod okunabilir ve test edilebilir.
- (+) `SKIP LOCKED` sayesinde servisin birden fazla kopyası güvenle çalışabiliyor.
- (+) Kafka kapalıyken olaylar kaybolmuyor (`scripts/kafka-outage-test.sh` ile kanıtlandı).
- (−) **At-least-once:** Kafka'ya gönderip `published_at` yazamadan çökersek olay tekrar gider.
  Tüketiciler idempotent olmak zorunda.
- (−) Polling aralığı kadar ek gecikme (en fazla ~500 ms) ve boşta da sürekli sorgu.
- (−) Gönderim sırasında veritabanı transaction'ı açık kalıyor. Kafka yavaşsa kilitler uzun sürer.
- **Alternatif:** Yük arttığında Debezium (CDC) ile değiştirilebilir. Tablo şeması bunu destekliyor.
