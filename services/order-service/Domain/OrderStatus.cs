namespace OrderService.Domain;

public enum OrderStatus
{
    Pending,
    StockReserved,
    Paid,
    Confirmed,
    Cancelling,
    Cancelled
}
