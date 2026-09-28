using System.Text.Json;

namespace OrderPlatform.Contracts;

/// <summary>
/// Tüm olayların ortak zarfı. Payload değişse bile tüketiciler
/// eventId (idempotency), correlationId (izleme) ve version (şema evrimi)
/// alanlarına her zaman aynı yerden ulaşır.
/// </summary>
public sealed record EventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    TPayload Payload);

public static class EventEnvelope
{
    public static EventEnvelope<TPayload> Create<TPayload>(
        string eventType,
        string correlationId,
        TPayload payload,
        int version = 1) =>
        new(Guid.NewGuid(), eventType, version, DateTimeOffset.UtcNow, correlationId, payload);
}

/// <summary>Olaylar her serviste aynı JSON ayarlarıyla (camelCase) yazılıp okunur.</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
