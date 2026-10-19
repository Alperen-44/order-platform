using Microsoft.EntityFrameworkCore;
using PaymentService.Persistence;

namespace PaymentService.Features;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var payments = app.MapGroup("/payments").WithTags("Payments");

        payments.MapGet("/", async (PaymentDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Payments.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .Take(50)
                .ToListAsync(ct)));

        payments.MapGet("/{orderId:guid}", async (Guid orderId, PaymentDbContext db, CancellationToken ct) =>
        {
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
            return payment is null ? Results.NotFound() : Results.Ok(payment);
        });

        return app;
    }
}
