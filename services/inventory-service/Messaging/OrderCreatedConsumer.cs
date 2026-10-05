using Confluent.Kafka;
using InventoryService.Features.Reservations;
using InventoryService.Persistence;
using Microsoft.Extensions.Options;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;

namespace InventoryService.Messaging;

/// <summary>
/// order.events topic'ini dinler. Her OrderCreated olayı için stok rezerve eder ve sonucu
/// (StockReserved / StockReservationFailed) outbox üzerinden inventory.events'e yayınlar.
/// Rezervasyon, outbox kaydı ve processed_events kaydı tek transaction'da yazılır.
/// </summary>
public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger<OrderCreatedConsumer> logger)
    : KafkaConsumerService<InventoryDbContext>(scopeFactory, kafkaOptions, producer, logger)
{
    protected override string Topic => Topics.OrderEvents;
    protected override string GroupId => "inventory-service";

    protected override async Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct)
    {
        // Bu topic'te ileride başka olay tipleri de olacak; ilgilenmediklerimizi atlıyoruz.
        if (message.EventType != EventTypes.OrderCreated) return;

        var envelope = message.Deserialize<OrderCreated>();
        var order = envelope.Payload;

        var reservations = services.GetRequiredService<StockReservationService>();
        var lines = order.Items.Select(i => new ReserveLine(i.ProductId, i.Quantity)).ToList();

        var result = await reservations.ReserveAsync(
            order.OrderId, lines, publishEvents: true, envelope.CorrelationId, ct);

        Logger.LogInformation("Order {OrderId}: stock reservation {Outcome}", order.OrderId, result.Outcome);
    }
}
