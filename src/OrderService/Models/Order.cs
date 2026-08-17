namespace OrderService.Models;

public class Order
{
    public Guid Id { get; set; }

    // This comes from the authenticated user's JWT.
    public Guid UserId { get; set; }

    public decimal TotalAmount { get; set; }

    public OrderStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; }
        = new List<OrderItem>();
}