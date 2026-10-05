using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderPlatform.Messaging.Outbox;

namespace OrderPlatform.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>Kafka ayarlarını ve uygulama boyunca tek bir producer'ı kaydeder.</summary>
    public static IServiceCollection AddKafka(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));

        services.AddSingleton<IProducer<string, string>>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
            var config = new ProducerConfig
            {
                BootstrapServers = options.BootstrapServers,
                Acks = Acks.All,              // mesaj tüm replikalara yazılınca "tamam" say
                EnableIdempotence = true,     // ağ hatasında yeniden gönderimde Kafka'da kopya oluşmasın
                MessageTimeoutMs = 10_000     // Kafka kapalıysa 10 sn'de vazgeç; olay outbox'ta bekler
            };
            return new ProducerBuilder<string, string>(config).Build();
        });

        return services;
    }

    /// <summary>Verilen DbContext'in outbox tablosunu verilen topic'e taşıyan relay'i başlatır.</summary>
    public static IServiceCollection AddOutboxRelay<TDbContext>(this IServiceCollection services, string topic)
        where TDbContext : DbContext
    {
        var options = new OutboxRelayOptions { Topic = topic };

        services.AddHostedService(sp => new OutboxRelay<TDbContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IProducer<string, string>>(),
            options,
            sp.GetRequiredService<ILogger<OutboxRelay<TDbContext>>>()));

        return services;
    }
}
