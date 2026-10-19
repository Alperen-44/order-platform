using Microsoft.EntityFrameworkCore;
using NotificationService.Persistence;

namespace NotificationService.Features;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /notifications              → son 50 bildirim
        // GET /notifications?orderId=...  → bir siparişin bildirimleri
        app.MapGet("/notifications", async (Guid? orderId, NotificationDbContext db, CancellationToken ct) =>
        {
            var query = db.Notifications.AsNoTracking();
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
