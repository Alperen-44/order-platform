using Microsoft.EntityFrameworkCore;
using NotificationService.Persistence;
using OrderPlatform.Contracts;

namespace NotificationService.Features;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /notifications              → son 50 bildirim
        // GET /notifications?orderId=...  → bir siparişin bildirimleri
        // Gateway'den gelen isteklerde yalnızca giriş yapan müşterinin bildirimleri görünür.
        app.MapGet("/notifications", async (Guid? orderId, HttpRequest http, NotificationDbContext db, CancellationToken ct) =>
        {
            var query = db.Notifications.AsNoTracking();

            if (Guid.TryParse(http.Headers[PlatformHeaders.CustomerId].ToString(), out var customerId))
                query = query.Where(n => n.CustomerId == customerId);

            if (orderId is not null)
                query = query.Where(n => n.OrderId == orderId.Value);

            return Results.Ok(await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .ToListAsync(ct));
        }).WithTags("Notifications");

        return app;
    }
}
