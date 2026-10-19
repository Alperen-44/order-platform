using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderPlatform.Contracts;
using OrderPlatform.Messaging;
using OrderPlatform.Messaging.Consuming;
using OrderPlatform.Messaging.Outbox;
using PaymentService.Domain;
using PaymentService.Persistence;
using PaymentService.Provider;

namespace PaymentService.Messaging;

/// <summary>
/// order.events topic'ini dinler:
///   PaymentRequested → ödemeyi al, sonucu (PaymentCompleted / PaymentFailed) yayınla
///   OrderCancelled   → TELAFİ: bu sipariş için alınmış bir ödeme varsa iade et (PaymentRefunded)
///
/// İade senaryosu: Ödeme çok geç sonuçlanırsa Order servisi siparişi zaman aşımıyla iptal eder.
/// Aynı siparişin olayları aynı partition'da sırayla geldiği için Payment önce ödemeyi alır,
/// ardından OrderCancelled'ı görür ve parayı iade eder. Müşteri iptal edilmiş bir sipariş için ödeme yapmış olmaz.
/// </summary>
public sealed class OrderEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IProducer<string, string> producer,
    ILogger<OrderEventsConsumer> logger)
    : KafkaConsumerService<PaymentDbContext>(scopeFactory, kafkaOptions, producer, logger)
{
    protected override string[] SubscribedTopics => [Topics.OrderEvents];
    protected override string GroupId => "payment-service";

    protected override Task HandleAsync(ConsumedEvent message, IServiceProvider services, CancellationToken ct) =>
        message.EventType switch
        {
            EventTypes.PaymentRequested => ChargeAsync(message.Deserialize<PaymentRequested>(), services, ct),
            EventTypes.OrderCancelled => RefundAsync(message.Deserialize<OrderCancelled>(), services, ct),
            _ => Task.CompletedTask
        };

    private async Task ChargeAsync(
        EventEnvelope<PaymentRequested> envelope, IServiceProvider services, CancellationToken ct)
    {
        var request = envelope.Payload;
        var db = services.GetRequiredService<PaymentDbContext>();
        var provider = services.GetRequiredService<FakePaymentProvider>();

        // İş kuralı seviyesinde güvenlik: bir siparişten iki kez ödeme alınmaz (UNIQUE order_id de var).
        if (await db.Payments.AnyAsync(p => p.OrderId == request.OrderId, ct))
        {
            Logger.LogInformation("Order {OrderId} already has a payment; skipping", request.OrderId);
            return;
        }

        var result = await provider.ChargeAsync(request.OrderId.ToString(), request.CustomerId, request.Amount, ct);

        Payment payment;
        OutboxMessage resultEvent;

        if (result.Success)
        {
            payment = Payment.Succeeded(request.OrderId, request.CustomerId, request.Amount, result.Reference!);
            resultEvent = OutboxMessage.From(request.OrderId, EventEnvelope.Create(
                EventTypes.PaymentCompleted, envelope.CorrelationId,
                new PaymentCompleted(request.OrderId, payment.Id, payment.Amount)));
        }
        else
        {
            payment = Payment.Declined(request.OrderId, request.CustomerId, request.Amount, result.FailureReason!);
            resultEvent = OutboxMessage.From(request.OrderId, EventEnvelope.Create(
                EventTypes.PaymentFailed, envelope.CorrelationId,
                new PaymentFailed(request.OrderId, result.FailureReason!)));
        }

        // Ödeme kaydı ve sonuç olayı aynı transaction'da (outbox pattern).
        db.Payments.Add(payment);
        db.OutboxMessages.Add(resultEvent);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Order {OrderId}: payment {Status}", request.OrderId, payment.Status);
    }

    private async Task RefundAsync(
        EventEnvelope<OrderCancelled> envelope, IServiceProvider services, CancellationToken ct)
    {
        var cancelled = envelope.Payload;
        var db = services.GetRequiredService<PaymentDbContext>();

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == cancelled.OrderId, ct);
        if (payment is null || !payment.Refund())
            return; // ödeme hiç alınmamış, başarısız olmuş ya da zaten iade edilmiş

        db.OutboxMessages.Add(OutboxMessage.From(cancelled.OrderId, EventEnvelope.Create(
            EventTypes.PaymentRefunded, envelope.CorrelationId,
            new PaymentRefunded(cancelled.OrderId, payment.CustomerId, payment.Id, payment.Amount))));
        await db.SaveChangesAsync(ct);

        Logger.LogWarning("Order {OrderId} was cancelled ({Reason}) after payment; refunded {Amount}",
            cancelled.OrderId, cancelled.Reason, payment.Amount);
    }
}
