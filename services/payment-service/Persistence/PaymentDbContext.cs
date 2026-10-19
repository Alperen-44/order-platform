using Microsoft.EntityFrameworkCore;
using OrderPlatform.Messaging.Consuming;
using OrderPlatform.Messaging.Outbox;
using PaymentService.Domain;

namespace PaymentService.Persistence;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.OrderId).IsUnique(); // bir siparişe en fazla bir ödeme
            e.Property(p => p.Amount).HasPrecision(12, 2);
            e.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(p => p.ProviderReference).HasMaxLength(64);
            e.Property(p => p.FailureReason).HasMaxLength(500);
        });

        modelBuilder.AddOutbox();
        modelBuilder.AddProcessedEvents();
    }
}
