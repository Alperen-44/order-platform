using OrderService.Domain;

namespace OrderService.Features.Orders;

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record CreateOrderRequest(Guid CustomerId, List<CreateOrderItemRequest>? Items)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (CustomerId == Guid.Empty)
            errors["customerId"] = ["customerId boş olamaz."];

        if (Items is null || Items.Count == 0)
        {
            errors["items"] = ["Sipariş en az bir kalem içermelidir."];
            return errors;
        }

        if (Items.Any(i => i.ProductId == Guid.Empty))
            errors["items.productId"] = ["productId boş olamaz."];
        if (Items.Any(i => i.Quantity <= 0))
            errors["items.quantity"] = ["quantity 0'dan büyük olmalıdır."];
        if (Items.Any(i => i.UnitPrice < 0))
            errors["items.unitPrice"] = ["unitPrice negatif olamaz."];
        if (Items.Select(i => i.ProductId).Distinct().Count() != Items.Count)
            errors["items"] = ["Aynı ürün birden fazla satırda olamaz; adedi artırın."];

        return errors;
    }
}

public sealed record OrderItemResponse(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status,
        order.TotalAmount,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice)).ToList());
}
