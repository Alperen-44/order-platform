using Microsoft.EntityFrameworkCore;
using OrderPlatform.Messaging.Consuming;
using OrderPlatform.Messaging.Outbox;
using OrderService.Domain;

namespace OrderService.Persistence;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Kolon adları UseSnakeCaseNamingConvention() ile otomatik: TotalAmount -> total_amount
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(o => o.Id);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(o => o.TotalAmount).HasPrecision(12, 2);
            e.Property(o => o.CancellationReason).HasMaxLength(500);
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            e.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.UnitPrice).HasPrecision(12, 2);
        });

        modelBuilder.AddOutbox();
        modelBuilder.AddProcessedEvents();
    }
}
