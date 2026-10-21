using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderPlatform.Contracts;
using OrderService.Domain;
using OrderService.Messaging;
using OrderService.Persistence;

namespace OrderService.Features.Orders;

public static class OrderEndpoints
{
    private const int MaxIdempotencyKeyLength = 100;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");
        orders.MapPost("/", CreateOrder).WithName("CreateOrder");
        orders.MapGet("/", ListMyOrders).WithName("ListMyOrders");
        orders.MapGet("/{id:guid}", GetOrder).WithName("GetOrder");

        // Yalnızca geliştirme ortamında: outbox'ta neler biriktiğini görmek için (gateway'den erişilemez)
        var debug = app.MapGroup("/debug").WithTags("Debug");
        debug.MapGet("/outbox", GetOutbox);

        return app;
    }

    /// <summary>
    /// Gateway'den gelen isteklerde müşteri kimliği X-Customer-Id başlığındadır (token'dan üretilir).
    /// Başlık yoksa istek servise doğrudan gelmiştir (geliştirme, test scriptleri).
    /// </summary>
    private static Guid? CustomerFromGateway(HttpRequest http) =>
        Guid.TryParse(http.Headers[PlatformHeaders.CustomerId].ToString(), out var id) ? id : null;

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request, HttpContext http, OrderDbContext db, CancellationToken ct)
    {
        // Giriş yapmış kullanıcı varsa gövdedeki customerId yok sayılır: kimse başkası adına sipariş veremez.
        if (CustomerFromGateway(http.Request) is { } customerId)
            request = request with { CustomerId = customerId };

        var errors = request.Validate();
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // ---------- Idempotency-Key ----------
        var idempotencyKey = http.Request.Headers[PlatformHeaders.IdempotencyKey].ToString();
        var hasKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        var requestHash = hasKey ? IdempotencyRecord.HashOf(request) : null;

        if (hasKey)
        {
            if (idempotencyKey.Length > MaxIdempotencyKeyLength)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [PlatformHeaders.IdempotencyKey] = [$"En fazla {MaxIdempotencyKeyLength} karakter olabilir."]
                });

            var previous = await FindIdempotencyRecordAsync(db, request.CustomerId, idempotencyKey, ct);
            if (previous is not null)
                return await ReplayAsync(previous, requestHash!, http, db, ct);
        }

        var order = Order.Create(
            request.CustomerId,
            request.Items!.Select(i => new OrderLineInput(i.ProductId, i.Quantity)));

        // KRİTİK NOKTA: Sipariş, olay ve idempotency kaydı tek bir SaveChanges ile, yani TEK transaction
        // içinde yazılır. Ya hepsi kalıcı olur ya hiçbiri.
        db.Orders.Add(order);
        db.OutboxMessages.Add(OrderOutboxEvents.Created(order));
        if (hasKey)
            db.IdempotencyRecords.Add(new IdempotencyRecord(request.CustomerId, idempotencyKey, requestHash!, order.Id));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (hasKey && IsIdempotencyConflict(ex))
        {
            // Aynı anahtarla iki istek AYNI ANDA geldi; ikisi de yukarıdaki kontrolü geçti.
            // Birincil anahtar (customer_id, key) ikincisini durdurdu: kazananın siparişini dön.
            db.ChangeTracker.Clear();
            var winner = await FindIdempotencyRecordAsync(db, request.CustomerId, idempotencyKey, ct);
            return await ReplayAsync(winner!, requestHash!, http, db, ct);
        }

        return Results.CreatedAtRoute("GetOrder", new { id = order.Id }, OrderResponse.From(order));
    }

    private static Task<IdempotencyRecord?> FindIdempotencyRecordAsync(
        OrderDbContext db, Guid customerId, string key, CancellationToken ct) =>
        db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Key == key, ct);

    private static async Task<IResult> ReplayAsync(
        IdempotencyRecord record, string requestHash, HttpContext http, OrderDbContext db, CancellationToken ct)
    {
        if (record.RequestHash != requestHash)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Idempotency-Key başka bir istek için kullanılmış",
                detail: "Aynı anahtar farklı bir sipariş içeriğiyle gönderildi. Yeni sipariş için yeni bir anahtar üretin.");
        }

        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstAsync(o => o.Id == record.OrderId, ct);

        http.Response.Headers[PlatformHeaders.IdempotentReplayed] = "true";
        return Results.Ok(OrderResponse.From(order));
    }

    private static bool IsIdempotencyConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "idempotency_keys" };

    private static async Task<IResult> ListMyOrders(HttpRequest http, OrderDbContext db, CancellationToken ct)
    {
        if (CustomerFromGateway(http) is not { } customerId)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"{PlatformHeaders.CustomerId} başlığı gerekli (istek gateway üzerinden gelmeli)");

        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Results.Ok(orders.Select(OrderResponse.From));
    }

    private static async Task<IResult> GetOrder(Guid id, HttpRequest http, OrderDbContext db, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        // Başkasının siparişi "yok" gibi görünür (403 dönmek siparişin var olduğunu ele verirdi).
        if (order is null || (CustomerFromGateway(http) is { } customerId && order.CustomerId != customerId))
            return Results.NotFound();

        return Results.Ok(OrderResponse.From(order));
    }

    private static async Task<IResult> GetOutbox(
        IHostEnvironment env, OrderDbContext db, CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        var messages = await db.OutboxMessages
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .Select(m => new { m.Id, m.AggregateId, m.EventType, m.CreatedAt, m.PublishedAt, m.Payload })
            .ToListAsync(ct);

        return Results.Ok(messages);
    }
}
