using Microsoft.EntityFrameworkCore;

namespace OrderPlatform.Messaging.Consuming;

/// <summary>
/// Bir consumer group'un işlediği olayların kaydı (idempotent consumer / "inbox" kaydı).
/// İş mantığının yaptığı değişiklikle AYNI transaction'da yazılır: ya ikisi birden kalıcı olur
/// ya da hiçbiri. Aynı eventId tekrar gelirse bu tabloya bakılıp atlanır.
/// </summary>
public class ProcessedEvent
{
    private ProcessedEvent() { } // EF Core için

    public ProcessedEvent(string consumerGroup, Guid eventId, string eventType)
    {
        ConsumerGroup = consumerGroup;
        EventId = eventId;
        EventType = eventType;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public string ConsumerGroup { get; private set; } = default!;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = default!;
    public DateTimeOffset ProcessedAt { get; private set; }
}

public static class ProcessedEventModelBuilderExtensions
{
    public static ModelBuilder AddProcessedEvents(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events");
            e.HasKey(p => new { p.ConsumerGroup, p.EventId });
            e.Property(p => p.ConsumerGroup).HasMaxLength(100);
            e.Property(p => p.EventType).HasMaxLength(100);
        });
        return modelBuilder;
    }
}
