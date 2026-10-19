using InventoryService.Features.Reservations;
using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;

namespace InventoryService.IntegrationTests;

/// <summary>
/// Stok rezervasyonunun veritabanı seviyesindeki garantileri. Bunlar ancak gerçek PostgreSQL ile
/// test edilebilir: atomik UPDATE, UNIQUE kısıtı, SAVEPOINT ve satır kilitleri.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StockReservationTests(PostgresFixture db)
{
    private static async Task<ReservationResult> ReserveAsync(
        PostgresFixture fixture, Guid orderId, IReadOnlyList<ReserveLine> lines, bool publishEvents = false)
    {
        await using var context = fixture.CreateDbContext();
        return await new StockReservationService(context)
            .ReserveAsync(orderId, lines, publishEvents, orderId.ToString(), CancellationToken.None);
    }

    // ---------- Eşzamanlılık ----------

    [Fact]
    public async Task Stokta_tek_urun_varken_50_es_zamanli_istekten_yalnizca_biri_kazanir()
    {
        // race-test.sh'in C# hali. "Önce oku, sonra yaz" yapılsaydı birden fazla kazanan olurdu.
        var productId = await db.CreateProductAsync(stock: 1);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            await start.Task; // hepsi aynı anda başlasın
            var result = await ReserveAsync(db, Guid.NewGuid(), [new ReserveLine(productId, 1)]);
            return result.Outcome;
        })).ToList();

        start.SetResult();
        var outcomes = await Task.WhenAll(requests);

        outcomes.Count(o => o == ReservationOutcome.Reserved).Should().Be(1);
        outcomes.Count(o => o == ReservationOutcome.InsufficientStock).Should().Be(49);

        var stock = await db.GetStockAsync(productId);
        stock.Available.Should().Be(0);
        stock.Reserved.Should().Be(1);
    }

    [Fact]
    public async Task Ayni_siparis_icin_es_zamanli_iki_istek_stogu_yalnizca_bir_kez_dusurur()
    {
        // Kafka aynı OrderCreated'ı iki consumer'a aynı anda teslim etmiş gibi.
        // Biri kazanır; diğeri UNIQUE(order_id, product_id) kısıtına takılır ve SAVEPOINT'e geri döner.
        var productId = await db.CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();

        var outcomes = await Task.WhenAll(
            Task.Run(() => ReserveAsync(db, orderId, [new ReserveLine(productId, 1)])),
            Task.Run(() => ReserveAsync(db, orderId, [new ReserveLine(productId, 1)])));

        outcomes.Select(r => r.Outcome).Should().BeEquivalentTo(
            new[] { ReservationOutcome.Reserved, ReservationOutcome.AlreadyReserved });
        (await db.GetStockAsync(productId)).Available.Should().Be(9);
    }

    // ---------- Idempotency ve ya hep ya hiç ----------

    [Fact]
    public async Task Ayni_siparis_tekrar_rezerve_edilirse_stok_ikinci_kez_dusmez()
    {
        var productId = await db.CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();

        var first = await ReserveAsync(db, orderId, [new ReserveLine(productId, 3)]);
        var second = await ReserveAsync(db, orderId, [new ReserveLine(productId, 3)]);

        first.Outcome.Should().Be(ReservationOutcome.Reserved);
        second.Outcome.Should().Be(ReservationOutcome.AlreadyReserved);

        var stock = await db.GetStockAsync(productId);
        stock.Available.Should().Be(7);
        stock.Reserved.Should().Be(3);
    }

    [Fact]
    public async Task Kalemlerden_biri_yetersizse_hicbir_kalem_rezerve_edilmez()
    {
        var plenty = await db.CreateProductAsync(stock: 10);
        var scarce = await db.CreateProductAsync(stock: 1);
        var orderId = Guid.NewGuid();

        var result = await ReserveAsync(db, orderId,
            [new ReserveLine(plenty, 2), new ReserveLine(scarce, 5)]);

        result.Outcome.Should().Be(ReservationOutcome.InsufficientStock);
        result.Reservations.Should().BeEmpty();

        // İlk kalemin düşüşü de SAVEPOINT ile geri alınmış olmalı.
        (await db.GetStockAsync(plenty)).Available.Should().Be(10);
        (await db.GetStockAsync(scarce)).Available.Should().Be(1);
    }

    [Fact]
    public async Task Olmayan_urun_icin_ProductNotFound_doner()
    {
        var result = await ReserveAsync(db, Guid.NewGuid(), [new ReserveLine(Guid.NewGuid(), 1)]);

        result.Outcome.Should().Be(ReservationOutcome.ProductNotFound);
    }

    // ---------- Telafi ve kesinleştirme ----------

    [Fact]
    public async Task Release_iki_kez_cagrilsa_bile_stok_bir_kez_geri_gelir()
    {
        var productId = await db.CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(db, orderId, [new ReserveLine(productId, 4)]);

        for (var i = 0; i < 2; i++)
        {
            await using var context = db.CreateDbContext();
            await new StockReservationService(context).ReleaseAsync(orderId, CancellationToken.None);
        }

        var stock = await db.GetStockAsync(productId);
        stock.Available.Should().Be(10);
        stock.Reserved.Should().Be(0);
    }

    [Fact]
    public async Task Kesinlesen_rezervasyon_reserved_dan_duser_ve_sonradan_geri_birakilamaz()
    {
        var productId = await db.CreateProductAsync(stock: 10);
        var orderId = Guid.NewGuid();
        await ReserveAsync(db, orderId, [new ReserveLine(productId, 2)]);

        await using (var context = db.CreateDbContext())
            (await new StockReservationService(context).CommitAsync(orderId, CancellationToken.None)).Should().Be(1);

        var afterCommit = await db.GetStockAsync(productId);
        afterCommit.Available.Should().Be(8); // satıldı
        afterCommit.Reserved.Should().Be(0);

        // Onaylanmış siparişe geç bir OrderCancelled gelirse satılmış stok geri dönmemeli.
        await using (var context = db.CreateDbContext())
            await new StockReservationService(context).ReleaseAsync(orderId, CancellationToken.None);

        (await db.GetStockAsync(productId)).Available.Should().Be(8);
    }

    // ---------- Outbox ----------

    [Fact]
    public async Task Basarili_rezervasyon_StockReserved_olayini_ayni_transactionda_outboxa_yazar()
    {
        var productId = await db.CreateProductAsync(stock: 5);
        var orderId = Guid.NewGuid();

        await ReserveAsync(db, orderId, [new ReserveLine(productId, 1)], publishEvents: true);

        await using var context = db.CreateDbContext();
        var events = await context.OutboxMessages.AsNoTracking()
            .Where(m => m.AggregateId == orderId).ToListAsync();

        events.Should().ContainSingle()
            .Which.EventType.Should().Be(EventTypes.StockReserved);
        events[0].PublishedAt.Should().BeNull(); // relay henüz göndermedi
    }

    [Fact]
    public async Task Yetersiz_stokta_rezervasyon_yazilmaz_ama_StockReservationFailed_olayi_yazilir()
    {
        var productId = await db.CreateProductAsync(stock: 0);
        var orderId = Guid.NewGuid();

        var result = await ReserveAsync(db, orderId, [new ReserveLine(productId, 1)], publishEvents: true);

        result.Outcome.Should().Be(ReservationOutcome.InsufficientStock);

        await using var context = db.CreateDbContext();
        (await context.Reservations.AnyAsync(r => r.OrderId == orderId)).Should().BeFalse();
        (await context.OutboxMessages.SingleAsync(m => m.AggregateId == orderId))
            .EventType.Should().Be(EventTypes.StockReservationFailed);
    }
}
