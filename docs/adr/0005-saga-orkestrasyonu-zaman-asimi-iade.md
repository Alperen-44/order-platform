# ADR 0005: Saga orkestrasyonu Order'da; zaman aşımı ve geç gelen ödemenin iadesi

**Durum:** Kabul edildi · **Tarih:** 2026-10-05

## Bağlam
Bir sipariş üç servise yayılıyor: stok (Inventory), ödeme (Payment), bildirim (Notification).
Her adım başarısız olabilir ya da hiç cevap gelmeyebilir. Dağıtık bir transaction (2PC) kullanmıyoruz
(bkz. ADR 0001), bu yüzden başarısızlıkları telafi işlemleriyle (compensation) geri almamız gerekiyor.

## Karar

### 1. Orkestrasyon, koreografi değil
Saga'nın akışını Order servisindeki durum makinesi yönetiyor (`SagaEventsConsumer`):

| Gelen olay | Sipariş durumu | Yayınlanan olay |
|---|---|---|
| `StockReserved` | Pending → StockReserved | `PaymentRequested` |
| `StockReservationFailed` | Pending → Cancelled | `OrderCancelled` |
| `PaymentCompleted` | StockReserved → Confirmed | `OrderConfirmed` |
| `PaymentFailed` | StockReserved → Cancelled | `OrderCancelled` |
| *(zaman aşımı)* | StockReserved → Cancelled | `OrderCancelled` |

Diğer servisler yalnızca kendi işlerini yapıp sonucu yayınlıyor. Akışın tamamı tek bir dosyada
okunabiliyor ve "bu sipariş neden iptal oldu?" sorusunun cevabı Order'ın veritabanında.

### 2. Telafi işlemleri `OrderCancelled` ile tetikleniyor
- **Inventory:** rezervasyonu geri bırakır (`Reserved → Released`, stok `available`'a döner).
- **Payment:** bu sipariş için alınmış bir ödeme varsa iade eder (`Completed → Refunded`).

Her iki işlem de idempotent: rezervasyon ya da ödeme yoksa hiçbir şey yapmaz.

### 3. Saga zaman aşımı
`PaymentTimeoutWatcher`, 15 saniyeden uzun süre `StockReserved` durumunda kalan siparişleri iptal eder.
Payment servisi çökse bile stok sonsuza kadar kilitli kalmaz.

### 4. Geç gelen ödeme iade ediliyor
Zaman aşımından sonra ödeme yine de başarıyla alınabilir. Bu durumda:
1. Order, `PaymentCompleted`'ı görür ama sipariş zaten `Cancelled`. Durum makinesi geçişi reddeder.
2. Payment, aynı siparişin `OrderCancelled` olayını görür (aynı partition, aynı sıra) ve parayı iade eder.

Müşteri iptal edilmiş bir sipariş için para ödemiş olarak kalmaz.

### 5. Optimistic concurrency
Zaman aşımı servisi ile ödeme consumer'ı aynı siparişi aynı anda değiştirebilir. PostgreSQL'in
`xmin` sistem kolonu concurrency token olarak kullanılıyor. Kaybeden taraf `DbUpdateConcurrencyException`
alır: zaman aşımı servisi o siparişi atlar, consumer ise mesajı tekrar dener ve güncel durumu görür.

## Sonuçlar
- (+) Saga'nın tüm hata yolları testlerle gösterilebiliyor (`payment-failure-test.sh`, `payment-timeout-test.sh`).
- (+) Hiçbir senaryoda stok kilitli kalmıyor ya da müşteriden haksız ödeme alınmıyor.
- (−) Order servisi diğer servislerin olaylarını tanımak zorunda (orkestratörün doğal bedeli).
- (−) Sahte ödeme sağlayıcısı consumer içinde senkron çağrılıyor. Yavaş bir ödeme aynı partition'daki
  diğer ödemeleri bekletiyor. Gerçek sistemde sağlayıcı çağrısı asenkron yapılır (webhook ile sonuç alınır).
