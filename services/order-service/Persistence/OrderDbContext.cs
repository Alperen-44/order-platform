using Microsoft.EntityFrameworkCore;
using OrderPlatform.Messaging.Consuming;
using OrderPlatform.Messaging.Outbox;
using OrderService.Domain;
using OrderService.Features.Orders;

namespace OrderService.Persistence;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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

            // Optimistic concurrency: PostgreSQL'in her satırda tuttuğu xmin sistem kolonu.
            // Tabloda ayrıca bir kolon açmaya gerek yok.
            e.Property(o => o.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            e.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(i => i.Id);
            e.Property(i => i.UnitPrice).HasPrecision(12, 2);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_keys");
            e.HasKey(r => new { r.CustomerId, r.Key }); // anahtar müşteri başına benzersiz
            e.Property(r => r.Key).HasMaxLength(100);
            e.Property(r => r.RequestHash).HasMaxLength(64);

            // EF'in INSERT sırasını doğru kurması için ilişkiyi bildiriyoruz: önce sipariş, sonra bu kayıt.
            e.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId);
        });

        modelBuilder.AddOutbox();
        modelBuilder.AddProcessedEvents();
    }
}
