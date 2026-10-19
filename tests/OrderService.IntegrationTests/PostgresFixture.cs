using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Messaging;
using OrderService.Persistence;
using Testcontainers.PostgreSql;

namespace OrderService.IntegrationTests;

/// <summary>
/// Testler başlamadan Docker'da geçici bir PostgreSQL açar ve Order servisinin gerçek init.sql şemasını kurar.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "init.sql"));
        var result = await _container.ExecScriptAsync(script);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"init.sql çalıştırılamadı: {result.Stderr}");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public OrderDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>POST /orders'ın yaptığı gibi: sipariş + OrderCreated olayı tek SaveChanges'ta.</summary>
    public async Task<Order> CreateOrderAsync(decimal unitPrice = 100m, int quantity = 2)
    {
        var order = Order.Create(Guid.NewGuid(), [new OrderLineInput(Guid.NewGuid(), quantity, unitPrice)]);

        await using var db = CreateDbContext();
        db.Orders.Add(order);
        db.OutboxMessages.Add(OrderOutboxEvents.Created(order));
        await db.SaveChangesAsync();

        return order;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
