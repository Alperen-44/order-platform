using InventoryService.Domain;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Features.Reservations;

public sealed record ReserveItemRequest(Guid ProductId, int Quantity);

public sealed record ReserveStockRequest(Guid OrderId, List<ReserveItemRequest>? Items);

public sealed record ReservationLineResponse(Guid ProductId, int Quantity, ReservationStatus Status);

public sealed record ReservationResponse(Guid OrderId, IReadOnlyList<ReservationLineResponse> Items)
{
    public static ReservationResponse From(Guid orderId, IEnumerable<StockReservation> reservations) =>
        new(orderId, reservations.Select(r => new ReservationLineResponse(r.ProductId, r.Quantity, r.Status)).ToList());
}

/// <summary>
/// Faz 1'de rezervasyon REST ile tetikleniyor. Faz 2'de aynı mantık
/// Kafka'dan gelen OrderCreated olayını dinleyen bir consumer'a taşınacak.
/// </summary>
public static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        var reservations = app.MapGroup("/reservations").WithTags("Reservations");
        reservations.MapPost("/", Reserve);
        reservations.MapGet("/{orderId:guid}", GetReservation);
        reservations.MapPost("/{orderId:guid}/release", Release);
        return app;
    }

    private static async Task<IResult> Reserve(
        ReserveStockRequest request, InventoryDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // 1) Idempotency: bu sipariş için zaten rezervasyon varsa aynı cevabı dön, tekrar düşme.
        var existing = await LoadReservations(db, request.OrderId, ct);
        if (existing.Count > 0)
            return Results.Ok(ReservationResponse.From(request.OrderId, existing));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // 2) Ürünleri hep aynı sırada kilitle (ProductId'ye göre). Farklı sıralarda kilitleyen
        //    iki istek birbirini beklerse deadlock olur; sabit sıra bunu engeller.
        foreach (var item in request.Items!.OrderBy(i => i.ProductId))
        {
            var productId = item.ProductId;
            var quantity = item.Quantity;

            // 3) Kontrol ve güncelleme TEK atomik SQL'de:
            //    UPDATE stock SET available = available - @q, reserved = reserved + @q
            //    WHERE product_id = @id AND available >= @q
            //    "Önce oku, sonra yaz" yapsaydık iki istek aynı son ürünü satabilirdi.
            var affected = await db.Stock
                .Where(s => s.ProductId == productId && s.Available >= quantity)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.Available, s => s.Available - quantity)
                    .SetProperty(s => s.Reserved, s => s.Reserved + quantity)
                    .SetProperty(s => s.UpdatedAt, now), ct);

            if (affected == 0)
            {
                // Tüm sipariş ya tamamen rezerve edilir ya hiç: önceki kalemleri de geri al.
                await tx.RollbackAsync(ct);

                var productExists = await db.Stock.AnyAsync(s => s.ProductId == productId, ct);
                return productExists
                    ? Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Yetersiz stok",
                        detail: $"{productId} ürünü için {quantity} adet stok yok.")
                    : Results.Problem(
                        statusCode: StatusCodes.Status404NotFound,
                        title: "Ürün bulunamadı",
                        detail: $"{productId} ürünü kayıtlı değil.");
            }

            db.Reservations.Add(new StockReservation(request.OrderId, productId, quantity));
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // 4) Aynı orderId ile iki istek AYNI ANDA geldiyse ikisi de 1. adımı geçebilir.
            //    Veritabanındaki UNIQUE(order_id, product_id) kısıtı ikincisini durdurur;
            //    biz de kazanan isteğin sonucunu döneriz.
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var winner = await LoadReservations(db, request.OrderId, ct);
            return Results.Ok(ReservationResponse.From(request.OrderId, winner));
        }

        var created = await LoadReservations(db, request.OrderId, ct);
        return Results.Created($"/reservations/{request.OrderId}", ReservationResponse.From(request.OrderId, created));
    }

    private static async Task<IResult> GetReservation(Guid orderId, InventoryDbContext db, CancellationToken ct)
    {
        var reservations = await LoadReservations(db, orderId, ct);
        return reservations.Count == 0
            ? Results.NotFound()
            : Results.Ok(ReservationResponse.From(orderId, reservations));
    }

    /// <summary>
    /// Telafi işlemi (compensation): ödeme başarısız olunca saga bunu çağıracak.
    /// Birden fazla kez çağrılması güvenlidir (idempotent).
    /// </summary>
    private static async Task<IResult> Release(Guid orderId, InventoryDbContext db, CancellationToken ct)
    {
        var reservations = await LoadReservations(db, orderId, ct);
        if (reservations.Count == 0) return Results.NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var reservation in reservations
                     .Where(r => r.Status == ReservationStatus.Reserved)
                     .OrderBy(r => r.ProductId))
        {
            var reservationId = reservation.Id;
            var productId = reservation.ProductId;
            var quantity = reservation.Quantity;

            // Durumu yalnızca hâlâ "Reserved" ise değiştir. Aynı anda gelen ikinci release
            // 0 satır etkiler ve stoğu ikinci kez geri eklemez.
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

        await tx.CommitAsync(ct);

        var updated = await LoadReservations(db, orderId, ct);
        return Results.Ok(ReservationResponse.From(orderId, updated));
    }

    private static Task<List<StockReservation>> LoadReservations(
        InventoryDbContext db, Guid orderId, CancellationToken ct) =>
        db.Reservations
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.ProductId)
            .ToListAsync(ct);

    private static Dictionary<string, string[]> Validate(ReserveStockRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.OrderId == Guid.Empty)
            errors["orderId"] = ["orderId boş olamaz."];

        if (request.Items is null || request.Items.Count == 0)
        {
            errors["items"] = ["En az bir kalem gereklidir."];
            return errors;
        }

        if (request.Items.Any(i => i.ProductId == Guid.Empty))
            errors["items.productId"] = ["productId boş olamaz."];
        if (request.Items.Any(i => i.Quantity <= 0))
            errors["items.quantity"] = ["quantity 0'dan büyük olmalıdır."];
        if (request.Items.Select(i => i.ProductId).Distinct().Count() != request.Items.Count)
            errors["items"] = ["Aynı ürün birden fazla satırda olamaz."];

        return errors;
    }
}
