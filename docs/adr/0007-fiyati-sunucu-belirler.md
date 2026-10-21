# ADR 0007: Sipariş tutarını istemci değil Inventory belirler

**Durum:** Kabul edildi · **Tarih:** 2026-10-21

## Bağlam
Faz 8'e kadar `POST /orders` isteğindeki her kalem `unitPrice` alanı taşıyordu ve Order Service tutarı
bu fiyatlarla hesaplıyordu. Arayüz doğru fiyatı gönderse de isteği tarayıcıda ya da `curl` ile değiştiren
biri klavyeyi 1 TL'ye sipariş edebiliyordu: istemciden gelen her veri değiştirilebilir.

Fiyat Inventory Service'te (`stock.price`) tutuluyor. Order Service'in bu fiyata ulaşması gerekiyor.

## Değerlendirilen seçenekler

### A) Order, sipariş alınırken Inventory'yi HTTP ile çağırır
- (+) Sipariş baştan doğru tutarla oluşur.
- (−) Inventory kapalıysa ya da yavaşsa sipariş alınamaz. Şimdiye kadar her servisin diğerlerini
  beklemeden çalışması için kurulan yapı (outbox, Kafka, saga) ilk adımda bozulur.
- (−) Fiyat okunduktan sonra stok ayrılana kadar fiyat değişebilir; iki ayrı okuma tutarsız olabilir.

### B) Inventory, stok ayırırken fiyatı olaya ekler (seçilen)
- Order yalnızca ürün ve adet kaydeder (`TotalAmount = 0`, durum `Pending`).
- Inventory stoğu ayırdığı transaction'da güncel fiyatları okur ve `StockReserved` olayının her kalemine
  `UnitPrice` ekler.
- Order, `MarkStockReserved(unitPrices)` ile fiyatları kalemlere yazar, tutarı hesaplar ve
  `PaymentRequested` olayını bu tutarla yayınlar.

### C) Order, ürün fiyatlarının bir kopyasını tutar (Inventory'nin `ProductPriceChanged` olaylarından)
- (+) Senkron bağımlılık yok, sipariş baştan fiyatlı.
- (−) Yeni bir olay türü, yeni bir tablo ve kopyanın güncel olmama riski. Bu proje için fazla.

## Karar
**B.** Fiyat, stoğun ayrıldığı anda ve aynı yerden okunuyor; sistem tamamen olay güdümlü kalıyor.

Ayrıntılar:
- `CreateOrderItemRequest` artık `UnitPrice` içermiyor. İstemci gönderse de JSON'dan okunmuyor, yani
  idempotency hash'ine de girmiyor.
- Bir kalemin fiyatı olayda yoksa `MarkStockReserved` hata fırlatıyor ve sipariş değişmiyor. Sessizce 0 TL
  kabul etmek yerine mesaj tekrar deneniyor, sonunda DLQ'ya düşüyor (bkz. ADR 0004).
- Tekrar gelen bir `StockReserved` olayı (Kafka'nın tekrarı) fiyatları değiştiremez: geçiş yalnızca
  `Pending` durumundan yapılabiliyor.

## Sonuçlar
- (+) İstemci tutarı etkileyemiyor. `gateway-test.sh` 11. adım bunu doğruluyor.
- (+) Inventory kapalıyken sipariş yine alınıyor; "Stok kontrol ediliyor" adımında bekliyor.
- (−) Sipariş oluşturulduğu anda tutar bilinmiyor. Arayüz sepette güncel fiyatlarla tahmini toplamı
  gösteriyor, kesin tutar stok ayrılınca sipariş sayfasında beliriyor.
- (−) Sepete eklendikten sonra fiyat değişirse müşteri sepette gördüğünden farklı tutar öder. Gerçek bir
  mağazada fiyat değişince müşteriye onay sorulur; burada kapsam dışı.
