using InventoryService.Domain;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace InventoryService.IntegrationTests;

/// <summary>
/// Testler başlamadan Docker'da geçici bir PostgreSQL açar ve servisin gerçek init.sql şemasını kurar.
/// Bütün test sınıfları aynı container'ı paylaşır; testler bitince container silinir.
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

    /// <summary>Servisin kendisiyle aynı ayarlarla yeni bir DbContext (her paralel istek kendi bağlantısını kullanır).</summary>
    public InventoryDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    /// <summary>
    /// Her test kendi ürününü oluşturur. Böylece testler birbirinin stoğunu etkilemez,
    /// veritabanını her testten sonra temizlemeye gerek kalmaz.
    /// </summary>
    public async Task<Guid> CreateProductAsync(int stock)
    {
        var productId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.Stock.Add(new StockItem(productId, $"TEST-{productId:N}", "Test ürünü", stock));
        await db.SaveChangesAsync();
        return productId;
    }

    public async Task<StockItem> GetStockAsync(Guid productId)
    {
        await using var db = CreateDbContext();
        return await db.Stock.AsNoTracking().SingleAsync(s => s.ProductId == productId);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
