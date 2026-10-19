using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;
using PaymentService.Persistence;

namespace PaymentService.Features;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var payments = app.MapGroup("/payments").WithTags("Payments");

        // Gateway'den gelen isteklerde yalnızca giriş yapan müşterinin ödemeleri görünür.
        payments.MapGet("/", async (HttpRequest http, PaymentDbContext db, CancellationToken ct) =>
        {
            var query = db.Payments.AsNoTracking();
            if (CustomerFromGateway(http) is { } customerId)
                query = query.Where(p => p.CustomerId == customerId);

            return Results.Ok(await query.OrderByDescending(p => p.CreatedAt).Take(50).ToListAsync(ct));
        });

        payments.MapGet("/{orderId:guid}", async (Guid orderId, HttpRequest http, PaymentDbContext db, CancellationToken ct) =>
        {
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);

            // Başkasının ödemesi "yok" gibi görünür.
            if (payment is null || (CustomerFromGateway(http) is { } customerId && payment.CustomerId != customerId))
                return Results.NotFound();

            return Results.Ok(payment);
        });

        return app;
    }

    private static Guid? CustomerFromGateway(HttpRequest http) =>
        Guid.TryParse(http.Headers[PlatformHeaders.CustomerId].ToString(), out var id) ? id : null;
}
