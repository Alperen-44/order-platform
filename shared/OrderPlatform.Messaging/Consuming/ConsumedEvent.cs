using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using OrderPlatform.Contracts;

namespace OrderPlatform.Messaging.Consuming;

/// <summary>Kafka'dan okunan bir mesajın, iş mantığının ihtiyaç duyduğu hali.</summary>
public sealed record ConsumedEvent(
    string? Key,
    string EventType,
    string EventId,
    string Payload,
    TopicPartitionOffset Position)
{
    public static ConsumedEvent From(ConsumeResult<string, string> result) => new(
        result.Message.Key,
        ReadHeader(result.Message.Headers, MessageHeaders.EventType) ?? "Unknown",
        ReadHeader(result.Message.Headers, MessageHeaders.EventId) ?? "unknown",
        result.Message.Value,
        result.TopicPartitionOffset);

    public EventEnvelope<TPayload> Deserialize<TPayload>() =>
        JsonSerializer.Deserialize<EventEnvelope<TPayload>>(Payload, EventJson.Options)
        ?? throw new InvalidOperationException($"Could not deserialize {EventType} event {EventId}");

    private static string? ReadHeader(Headers? headers, string key)
    {
        if (headers is not null && headers.TryGetLastBytes(key, out var bytes))
            return Encoding.UTF8.GetString(bytes);
        return null;
    }
}
