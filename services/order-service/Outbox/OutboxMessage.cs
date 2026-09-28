using System.Text.Json;
using OrderPlatform.Contracts;

namespace OrderService.Outbox;

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
