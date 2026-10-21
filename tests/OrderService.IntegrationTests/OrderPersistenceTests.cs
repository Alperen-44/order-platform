using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging.Outbox;
using OrderService.Domain;

namespace OrderService.IntegrationTests;

/// <summary>
/// Order servisinin veritabanı garantileri:
///   1) Outbox pattern: sipariş ve olay ya birlikte yazılır ya hiç yazılmaz.
///   2) Optimistic concurrency: aynı sipariş aynı anda iki yerden değiştirilirse biri hata alır.
/// </summary>
[Collection(PostgresCollection.Name)]
public class OrderPersistenceTests(PostgresFixture db)
{
    // ---------- Outbox pattern ----------

    [Fact]
    public async Task Siparis_ve_OrderCreated_olayi_birlikte_kaydedilir()
    {
        var order = await db.CreateOrderAsync(quantity: 2);

        await using var context = db.CreateDbContext();
        var saved = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        saved.Status.Should().Be(OrderStatus.Pending);
        saved.TotalAmount.Should().Be(0m);
        saved.Items.Should().ContainSingle();

        var outbox = await context.OutboxMessages.Where(m => m.AggregateId == order.Id).ToListAsync();
        outbox.Should().ContainSingle();
        outbox[0].EventType.Should().Be(EventTypes.OrderCreated);
        outbox[0].PublishedAt.Should().BeNull();
        outbox[0].Payload.Should().Contain(order.Id.ToString());
    }

    [Fact]
    public async Task Olay_yazilamazsa_siparis_de_yazilmaz()
    {
        // Outbox'a aynı eventId ile ikinci bir satır yazmaya çalışarak hatayı zorluyoruz.
        // Sipariş ile olay aynı transaction'da olduğu için olay patlayınca sipariş de geri alınmalı.
        var eventId = Guid.NewGuid();

        await using (var first = db.CreateDbContext())
        {
            first.OutboxMessages.Add(OutboxWithId(eventId, Guid.NewGuid()));
            await first.SaveChangesAsync();
        }

        var order = Order.Create(Guid.NewGuid(), [new OrderLineInput(Guid.NewGuid(), 1)]);
        await using (var second = db.CreateDbContext())
        {
            second.Orders.Add(order);
            second.OutboxMessages.Add(OutboxWithId(eventId, order.Id)); // aynı birincil anahtar

            var act = () => second.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using var check = db.CreateDbContext();
        (await check.Orders.AnyAsync(o => o.Id == order.Id))
            .Should().BeFalse("olay kaydedilemediyse sipariş de kaydedilmemeli");
    }

    [Fact]
    public async Task Stok_ayrilinca_fiyatlar_ve_tutar_kaydedilir()
    {
        var order = await db.CreateOrderAsync(quantity: 2);

        await using (var context = db.CreateDbContext())
        {
            var loaded = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            loaded.MarkStockReserved(PostgresFixture.PricesFor(loaded, unitPrice: 150m));
            await context.SaveChangesAsync();
        }

        await using var check = db.CreateDbContext();
        var saved = await check.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        saved.Status.Should().Be(OrderStatus.StockReserved);
        saved.Items.Single().UnitPrice.Should().Be(150m);
        saved.TotalAmount.Should().Be(300m);
    }

    // ---------- Optimistic concurrency (xmin) ----------

    [Fact]
    public async Task Her_guncellemede_Version_degisir()
    {
        var order = await db.CreateOrderAsync();

        await using var context = db.CreateDbContext();
        var loaded = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        var before = loaded.Version;

        loaded.MarkStockReserved(PostgresFixture.PricesFor(loaded));
        await context.SaveChangesAsync();

        loaded.Version.Should().NotBe(before);
    }

    [Fact]
    public async Task Ayni_siparisi_es_zamanli_degistiren_ikinci_islem_hata_alir()
    {
        // Senaryo: ödeme sonucu (consumer) ile zaman aşımı servisi aynı siparişi aynı anda okuyor.
        var order = await db.CreateOrderAsync();

        await using var consumer = db.CreateDbContext();
        await using var timeoutWatcher = db.CreateDbContext();

        var byConsumer = await consumer.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
        var byWatcher = await timeoutWatcher.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);

        // Consumer önce davranıp kaydediyor.
        byConsumer.MarkStockReserved(PostgresFixture.PricesFor(byConsumer));
        await consumer.SaveChangesAsync();

        // Zaman aşımı servisi eski bir kopyayı değiştirmeye çalışıyor.
        byWatcher.Cancel("Payment timeout");
        var act = () => timeoutWatcher.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Consumer'ın değişikliği sessizce ezilmedi.
        await using var check = db.CreateDbContext();
        var final = await check.Orders.SingleAsync(o => o.Id == order.Id);
        final.Status.Should().Be(OrderStatus.StockReserved);
        final.CancellationReason.Should().BeNull();
    }

    [Fact]
    public async Task Saga_durumlari_veritabanina_metin_olarak_kaydedilir()
    {
        var order = await db.CreateOrderAsync();

        await using (var context = db.CreateDbContext())
        {
            var loaded = await context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            loaded.MarkStockReserved(PostgresFixture.PricesFor(loaded));
            loaded.Confirm();
            await context.SaveChangesAsync();
        }

        // Ham SQL ile okuyunca "Confirmed" metni görünmeli (0, 1, 2 gibi sayılar değil).
        await using var check = db.CreateDbContext();
        var status = await check.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM orders WHERE id = {order.Id}")
            .SingleAsync();
        status.Should().Be("Confirmed");
    }

    private static OutboxMessage OutboxWithId(Guid eventId, Guid orderId) =>
        OutboxMessage.From(orderId, new EventEnvelope<OrderCreated>(
            eventId, EventTypes.OrderCreated, 1, DateTimeOffset.UtcNow, orderId.ToString(),
            new OrderCreated(orderId, Guid.NewGuid(), 10m, [])));
}
