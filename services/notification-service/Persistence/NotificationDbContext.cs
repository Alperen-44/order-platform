using Microsoft.EntityFrameworkCore;
using NotificationService.Domain;
using OrderPlatform.Messaging.Consuming;

namespace NotificationService.Persistence;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.OrderId);
            e.Property(n => n.Type).HasMaxLength(100);
            e.Property(n => n.Message).HasMaxLength(1000);
        });

        // Notification olay yayınlamıyor (outbox yok) ama aynı bildirimi iki kez göndermemek için
        // işlediği olayları kaydediyor.
        modelBuilder.AddProcessedEvents();
    }
}
