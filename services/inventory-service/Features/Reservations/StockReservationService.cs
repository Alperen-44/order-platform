using InventoryService.Domain;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging.Outbox;

namespace InventoryService.Features.Reservations;

public sealed record ReserveLine(Guid ProductId, int Quantity);

public enum ReservationOutcome
{
    Reserved,
    AlreadyReserved,
    InsufficientStock,
    ProductNotFound
}

public sealed record ReservationResult(
    ReservationOutcome Outcome,
    IReadOnlyList<StockReservation> Reservations,
    string? FailureReason = null);

/// <summary>
/// Stok rezervasyonunun tüm iş mantığı. Hem REST endpoint'i hem Kafka consumer'ı bu sınıfı kullanır.
///
/// Transaction yönetimi:
/// - REST'ten çağrılınca kendi transaction'ını açar ve commit eder.
/// - Kafka consumer'ından çağrılınca consumer'ın transaction'ına katılır (processed_events kaydıyla
///   birlikte commit edilsin diye). Kısmi geri alma için SAVEPOINT kullanır.
/// </summary>
public sealed class StockReservationService(InventoryDbContext db)
{
    private const string ReserveSavepoint = "reserve_stock";

    /// <param name="publishEvents">
    /// true ise sonuç (StockReserved / StockReservationFailed) outbox'a yazılır ve Order servisine gider.
    /// Kafka'dan gelen siparişlerde true, elle yapılan REST denemelerinde false.
    /// </param>
    public async Task<ReservationResult> ReserveAsync(
        Guid orderId,
        IReadOnlyList<ReserveLine> lines,
        bool publishEvents,
        string correlationId,
        CancellationToken ct)
    {
        // 1) İş kuralı seviyesinde idempotency: bu sipariş zaten rezerve edildiyse tekrar düşme.
        //    (Kafka tarafındaki tekrarları processed_events zaten yakalıyor; bu kontrol REST için de geçerli.)
        var existing = await LoadAsync(orderId, ct);
        if (existing.Count > 0)
            return new ReservationResult(ReservationOutcome.AlreadyReserved, existing);

        // Dışarıda açık bir transaction varsa ona katıl, yoksa kendi transaction'ını aç.
        await using var ownTx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        IDbContextTransaction tx = db.Database.CurrentTransaction!;

        // SAVEPOINT: yetersiz stokta yalnızca BU metodun yaptıklarını geri alabilmek için.
        // (Tüm transaction'ı geri alsaydık consumer'ın açtığı transaction da giderdi.)
        await tx.CreateSavepointAsync(ReserveSavepoint, ct);
        var now = DateTimeOffset.UtcNow;

        // 2) Ürünleri hep aynı sırada kilitle: deadlock önleme.
        foreach (var line in lines.OrderBy(l => l.ProductId))
        {
            var productId = line.ProductId;
            var quantity = line.Quantity;

            // 3) Kontrol + düşme tek atomik SQL'de.
            var affected = await db.Stock
                .Where(s => s.ProductId == productId && s.Available >= quantity)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Available, s => s.Available - quantity)
                    .SetProperty(s => s.Reserved, s => s.Reserved + quantity)
                    .SetProperty(s => s.UpdatedAt, now), ct);

            if (affected == 0)
            {
                // Ya hep ya hiç: önceki kalemlerin düşüşünü de geri al.
                await tx.RollbackToSavepointAsync(ReserveSavepoint, ct);
                db.ChangeTracker.Clear();

                var productExists = await db.Stock.AnyAsync(s => s.ProductId == productId, ct);
                var outcome = productExists ? ReservationOutcome.InsufficientStock : ReservationOutcome.ProductNotFound;
                var reason = productExists
                    ? $"Insufficient stock for product {productId} (requested {quantity})"
                    : $"Product {productId} not found";

                if (publishEvents)
                {
                    db.OutboxMessages.Add(OutboxMessage.From(orderId, EventEnvelope.Create(
                        EventTypes.StockReservationFailed, correlationId,
                        new StockReservationFailed(orderId, reason))));
                    await db.SaveChangesAsync(ct);
                }

                if (ownTx is not null) await ownTx.CommitAsync(ct);
                return new ReservationResult(outcome, [], reason);
            }

            db.Reservations.Add(new StockReservation(orderId, productId, quantity));
        }

        // 4) Rezervasyon kayıtları ile StockReserved olayı AYNI transaction'da yazılır (outbox pattern).
        if (publishEvents)
        {
            db.OutboxMessages.Add(OutboxMessage.From(orderId, EventEnvelope.Create(
                EventTypes.StockReserved, correlationId,
                new StockReserved(orderId, lines.Select(l => new ReservedLine(l.ProductId, l.Quantity)).ToList()))));
        }

        try
        {
            await db.SaveChangesAsync(ct);
            if (ownTx is not null) await ownTx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // 5) Aynı sipariş için eşzamanlı ikinci istek: UNIQUE(order_id, product_id) yakaladı.
            //    Bu metodun yaptığı stok düşüşlerini geri al, kazanan isteğin sonucunu dön.
            await tx.RollbackToSavepointAsync(ReserveSavepoint, CancellationToken.None);
            db.ChangeTracker.Clear();
            return new ReservationResult(ReservationOutcome.AlreadyReserved, await LoadAsync(orderId, ct));
        }

        return new ReservationResult(ReservationOutcome.Reserved, await LoadAsync(orderId, ct));
    }

    /// <summary>Telafi işlemi. Birden fazla kez çağrılması güvenlidir.</summary>
    public async Task<IReadOnlyList<StockReservation>?> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        var reservations = await LoadAsync(orderId, ct);
        if (reservations.Count == 0) return null;

        await using var ownTx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var now = DateTimeOffset.UtcNow;

        foreach (var reservation in reservations
                     .Where(r => r.Status == ReservationStatus.Reserved)
                     .OrderBy(r => r.ProductId))
        {
            var reservationId = reservation.Id;
            var productId = reservation.ProductId;
            var quantity = reservation.Quantity;

            // Yalnızca hâlâ "Reserved" ise değiştir: aynı anda gelen ikinci release 0 satır etkiler.
            var changed = await db.Reservations
                .Where(r => r.Id == reservationId && r.Status == ReservationStatus.Reserved)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Status, ReservationStatus.Released)
                    .SetProperty(r => r.UpdatedAt, now), ct);

            if (changed == 0) continue;

            await db.Stock
                .Where(s => s.ProductId == productId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Available, s => s.Available + quantity)
                    .SetProperty(s => s.Reserved, s => s.Reserved - quantity)
                    .SetProperty(s => s.UpdatedAt, now), ct);
        }

        if (ownTx is not null) await ownTx.CommitAsync(ct);
        return await LoadAsync(orderId, ct);
    }

    public Task<List<StockReservation>> LoadAsync(Guid orderId, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.ProductId)
            .ToListAsync(ct);
}
