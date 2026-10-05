namespace OrderPlatform.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Docker içinde "kafka:19092", bilgisayardan "localhost:9092".</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";
}

/// <summary>Kafka mesaj başlıkları (headers). Tüketici, payload'ı açmadan olay tipini buradan okur.</summary>
public static class MessageHeaders
{
    public const string EventType = "event-type";
    public const string EventId = "event-id";
}
