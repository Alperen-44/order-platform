# ADR 0002: Faz 1'de şema SQL init script'leriyle oluşturuluyor

**Durum:** Kabul edildi (geçici) · **Tarih:** 2026-09-28

## Bağlam
Şemayı EF Core migration'larıyla ya da düz SQL ile yönetebiliriz. Projenin başında tablolar
sık değişecek ve outbox gibi yapıların SQL karşılığını açıkça görmek öğretici.

## Karar
Faz 1'de her servisin `db/init.sql` dosyası, Postgres container'ı ilk kez başlarken
`/docker-entrypoint-initdb.d` üzerinden çalışıyor. EF Core yalnızca bu tablolara eşleniyor.

## Sonuçlar
- (+) Şema tek bakışta okunuyor; kısmi indeks, CHECK kısıtları gibi detaylar görünür.
- (−) Script yalnızca volume boşken çalışır. Şema değişince `docker compose down -v` gerekir.
- (−) Canlı ortamda sürümlü şema değişikliği yapılamaz.
- **Sonraki adım:** Şema oturduğunda EF Core migration'larına (veya Flyway/DbUp'a) geçilecek.
