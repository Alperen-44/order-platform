using InventoryService.Domain;

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
/// Faz 2'den itibaren rezervasyonlar Kafka'dan gelen OrderCreated olayıyla otomatik yapılıyor
/// (bkz. Messaging/OrderCreatedConsumer). Bu endpoint'ler elle deneme ve yarış testi için duruyor;
/// buradan yapılan rezervasyonlar olay YAYINLAMAZ.
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
        ReserveStockRequest request, StockReservationService service, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var lines = request.Items!.Select(i => new ReserveLine(i.ProductId, i.Quantity)).ToList();
        var result = await service.ReserveAsync(
            request.OrderId, lines, publishEvents: false, correlationId: request.OrderId.ToString(), ct);

        return result.Outcome switch
        {
            ReservationOutcome.Reserved => Results.Created(
                $"/reservations/{request.OrderId}", ReservationResponse.From(request.OrderId, result.Reservations)),
            ReservationOutcome.AlreadyReserved => Results.Ok(
                ReservationResponse.From(request.OrderId, result.Reservations)),
            ReservationOutcome.InsufficientStock => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Yetersiz stok", detail: result.FailureReason),
            _ => Results.Problem(
                statusCode: StatusCodes.Status404NotFound, title: "Ürün bulunamadı", detail: result.FailureReason)
        };
    }

    private static async Task<IResult> GetReservation(
        Guid orderId, StockReservationService service, CancellationToken ct)
    {
        var reservations = await service.LoadAsync(orderId, ct);
        return reservations.Count == 0
            ? Results.NotFound()
            : Results.Ok(ReservationResponse.From(orderId, reservations));
    }

    private static async Task<IResult> Release(
        Guid orderId, StockReservationService service, CancellationToken ct)
    {
        var updated = await service.ReleaseAsync(orderId, ct);
        return updated is null
            ? Results.NotFound()
            : Results.Ok(ReservationResponse.From(orderId, updated));
    }

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
