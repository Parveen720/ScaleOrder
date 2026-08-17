namespace OrderService.Models;

public enum OrderStatus
{
    Pending = 0,
    PaymentPending = 1,
    Paid = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6
}