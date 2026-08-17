using OrderService.Models;

namespace OrderService.DTOs;

public class OrderResponseDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public List<OrderItemResponseDto> Items { get; set; }
        = new();
}

public class OrderItemResponseDto
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }
}