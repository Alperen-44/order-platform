using InventoryService.Domain;
using InventoryService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Features.Products;

public sealed record CreateProductRequest(string? Sku, string? Name, int InitialStock);

public sealed record ProductResponse(
    Guid ProductId, string Sku, string Name, int Available, int Reserved, DateTimeOffset UpdatedAt)
{
    public static ProductResponse From(StockItem s) =>
        new(s.ProductId, s.Sku, s.Name, s.Available, s.Reserved, s.UpdatedAt);
}

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/products").WithTags("Products");
        products.MapGet("/", ListProducts);
        products.MapGet("/{id:guid}", GetProduct).WithName("GetProduct");
        products.MapPost("/", CreateProduct);
        return app;
    }

    private static async Task<IResult> ListProducts(InventoryDbContext db, CancellationToken ct)
    {
        var items = await db.Stock.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        return Results.Ok(items.Select(ProductResponse.From));
    }

    private static async Task<IResult> GetProduct(Guid id, InventoryDbContext db, CancellationToken ct)
    {
        var item = await db.Stock.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == id, ct);
        return item is null ? Results.NotFound() : Results.Ok(ProductResponse.From(item));
    }

    private static async Task<IResult> CreateProduct(
        CreateProductRequest request, InventoryDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Sku)) errors["sku"] = ["sku zorunludur."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["name zorunludur."];
        if (request.InitialStock < 0) errors["initialStock"] = ["initialStock negatif olamaz."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var item = new StockItem(Guid.NewGuid(), request.Sku!.Trim(), request.Name!.Trim(), request.InitialStock);
        db.Stock.Add(item);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Bu SKU zaten kayıtlı.");
        }

        return Results.CreatedAtRoute("GetProduct", new { id = item.ProductId }, ProductResponse.From(item));
    }
}
