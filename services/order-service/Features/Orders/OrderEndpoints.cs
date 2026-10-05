using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using OrderService.Domain;
using OrderPlatform.Messaging.Outbox;
using OrderService.Persistence;

namespace OrderService.Features.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");
        orders.MapPost("/", CreateOrder).WithName("CreateOrder");
        orders.MapGet("/{id:guid}", GetOrder).WithName("GetOrder");

        // Yalnızca geliştirme ortamında: outbox'ta neler biriktiğini görmek için
        var debug = app.MapGroup("/debug").WithTags("Debug");
        debug.MapGet("/outbox", GetOutbox);

        return app;
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request, OrderDbContext db, CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var order = Order.Create(
            request.CustomerId,
            request.Items!.Select(i => new OrderLineInput(i.ProductId, i.Quantity, i.UnitPrice)));

        var orderCreated = EventEnvelope.Create(
            EventTypes.OrderCreated,
            correlationId: order.Id.ToString(),
            new OrderCreated(
                order.Id,
                order.CustomerId,
                order.TotalAmount,
                order.Items.Select(i => new OrderLine(i.ProductId, i.Quantity, i.UnitPrice)).ToList()));

        // KRİTİK NOKTA: Sipariş ve olay tek bir SaveChanges ile, yani TEK transaction içinde yazılır.
        // Ya ikisi birden kalıcı olur ya da hiçbiri. "Sipariş var ama olay yok" durumu oluşamaz.
        db.Orders.Add(order);
        db.OutboxMessages.Add(OutboxMessage.From(order.Id, orderCreated));
        await db.SaveChangesAsync(ct);

        return Results.CreatedAtRoute("GetOrder", new { id = order.Id }, OrderResponse.From(order));
    }

    private static async Task<IResult> GetOrder(Guid id, OrderDbContext db, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
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
