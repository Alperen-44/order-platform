using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderPlatform.Contracts;

namespace OrderPlatform.Messaging.Outbox;

/// <summary>
/// Kafka'ya gönderilmeyi bekleyen bir olay. Her servis kendi veritabanında
/// aynı şemaya sahip bir "outbox" tablosu tutar.
/// </summary>
public class OutboxMessage
{
    private OutboxMessage() { } // EF Core için

    public Guid Id { get; private set; }
    public Guid AggregateId { get; private set; }
    public string EventType { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static OutboxMessage From<TPayload>(Guid aggregateId, EventEnvelope<TPayload> envelope) => new()
    {
        Id = envelope.EventId,
        AggregateId = aggregateId,
        EventType = envelope.EventType,
        Payload = JsonSerializer.Serialize(envelope, EventJson.Options),
        CreatedAt = envelope.OccurredAt
    };

    public void MarkPublished(DateTimeOffset at) => PublishedAt = at;
}

public static class OutboxModelBuilderExtensions
{
    /// <summary>Servisin DbContext'ine outbox tablosunu ekler.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(m => m.Id);
            e.Property(m => m.EventType).HasMaxLength(100);
            e.Property(m => m.Payload).HasColumnType("jsonb");
        });
        return modelBuilder;
    }
}
