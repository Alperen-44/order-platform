using InventoryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Persistence;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(e =>
        {
            e.ToTable("stock");
            e.HasKey(s => s.ProductId);
            e.Property(s => s.Sku).HasMaxLength(64);
            e.Property(s => s.Name).HasMaxLength(200);
            e.HasIndex(s => s.Sku).IsUnique();
        });

        modelBuilder.Entity<StockReservation>(e =>
        {
            e.ToTable("stock_reservations");
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(r => new { r.OrderId, r.ProductId }).IsUnique();
        });
    }
}
