# ADR 0001: Her servisin kendi veritabanı olacak

**Durum:** Kabul edildi · **Tarih:** 2026-09-28

## Bağlam
Order ve Inventory servisleri aynı veritabanını paylaşsaydı, sipariş + stok işlemini tek bir
transaction'da yapmak çok kolay olurdu. Ama bu durumda servisler şemaya sıkı sıkıya bağlanır:
biri tablo değiştirdiğinde diğeri bozulur, bağımsız deploy edilemezler ve ölçeklenemezler.

## Karar
Her servis kendi PostgreSQL veritabanına sahip. Hiçbir servis başka bir servisin veritabanına
bağlanmaz. Servisler arası tutarlılık dağıtık transaction (2PC) ile değil, olaylar ve
Saga pattern ile sağlanacak.

## Sonuçlar
- (+) Servisler bağımsız geliştirilir, deploy edilir, ölçeklenir.
- (−) "Sipariş oluştu ama stok düşmedi" gibi ara durumlar oluşabilir → eventual consistency.
- (−) Bu ara durumları yönetmek için outbox, idempotency ve telafi işlemleri gerekiyor
  (projenin asıl öğrenme konusu tam olarak bu).
