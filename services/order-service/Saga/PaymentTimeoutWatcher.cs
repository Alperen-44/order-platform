using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderService.Domain;
using OrderService.Messaging;
using OrderService.Persistence;

namespace OrderService.Saga;

public sealed class SagaOptions
{
    public const string SectionName = "Saga";

    /// <summary>Stok ayrıldıktan sonra ödeme için en fazla ne kadar beklenir.</summary>
    public int PaymentTimeoutSeconds { get; set; } = 15;

    /// <summary>Süresi dolan siparişler ne sıklıkla kontrol edilir.</summary>
    public int CheckIntervalSeconds { get; set; } = 2;
}

/// <summary>
/// Saga zaman aşımı. Payment servisi çökmüş ya da çok yavaşsa sipariş sonsuza kadar
/// "StockReserved" durumunda kalıp stoğu kilitlememeli. Süresi dolan siparişler iptal edilir
/// ve OrderCancelled olayı yayınlanır: Inventory stoğu geri bırakır.
/// Ödeme yine de sonradan alınırsa, Payment servisi OrderCancelled'ı görünce iade eder.
/// </summary>
public sealed class PaymentTimeoutWatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<SagaOptions> options,
    ILogger<PaymentTimeoutWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeout = TimeSpan.FromSeconds(options.Value.PaymentTimeoutSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.CheckIntervalSeconds));
        logger.LogInformation("Payment timeout watcher started (timeout: {Timeout})", timeout);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CancelExpiredAsync(timeout, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Payment timeout check failed, will retry");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Uygulama kapanıyor.
        }
    }

    private async Task CancelExpiredAsync(TimeSpan timeout, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var deadline = DateTimeOffset.UtcNow - timeout;
        var expired = await db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.StockReserved && o.UpdatedAt < deadline)
            .OrderBy(o => o.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var order in expired)
        {
            if (!order.Cancel($"Payment timeout: no payment result within {timeout.TotalSeconds:0}s"))
                continue;

            // Sipariş ve olay aynı SaveChanges'ta: tek transaction (outbox pattern).
            db.OutboxMessages.Add(OrderOutboxEvents.Cancelled(order));

            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Order {OrderId} cancelled: payment timeout", order.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Tam bu sırada ödeme sonucu geldi ve consumer siparişi değiştirdi. O kazandı.
                logger.LogInformation("Order {OrderId} changed concurrently; skipping timeout", order.Id);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged).ToList())
                    entry.State = EntityState.Detached;
            }
        }
    }
}
