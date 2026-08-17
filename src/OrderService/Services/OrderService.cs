using OrderService.DTOs;
using OrderService.Models;
using OrderService.Repositories;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;

    public OrderService(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(
        Guid userId,
        CreateOrderRequestDto request)
    {
        if (request.Items == null ||
            request.Items.Count == 0)
        {
            throw new ArgumentException(
                "Order must contain at least one item.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),

            UserId = userId,

            Status = OrderStatus.Pending,

            CreatedAt = DateTime.UtcNow,

            UpdatedAt = DateTime.UtcNow
        };

        foreach (var item in request.Items)
        {
            var totalPrice =
                item.Quantity * item.UnitPrice;

            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid(),

                OrderId = order.Id,

                ProductId = item.ProductId,

                Quantity = item.Quantity,

                UnitPrice = item.UnitPrice,

                TotalPrice = totalPrice
            };

            order.Items.Add(orderItem);
        }

        order.TotalAmount = order.Items
            .Sum(i => i.TotalPrice);

        await _orderRepository.AddAsync(order);

        await _orderRepository.SaveChangesAsync();

        return MapToResponse(order);
    }

    public async Task<OrderResponseDto?> GetOrderAsync(
        Guid orderId)
    {
        var order = await _orderRepository
            .GetOrderWithItemsAsync(orderId);

        if (order == null)
        {
            return null;
        }

        return MapToResponse(order);
    }

    public async Task<IEnumerable<OrderResponseDto>>
        GetUserOrdersAsync(Guid userId)
    {
        var orders = await _orderRepository
            .GetOrdersByUserIdAsync(userId);

        return orders.Select(MapToResponse);
    }

    private static OrderResponseDto MapToResponse(
        Order order)
    {
        return new OrderResponseDto
        {
            Id = order.Id,

            UserId = order.UserId,

            TotalAmount = order.TotalAmount,

            Status = order.Status.ToString(),

            CreatedAt = order.CreatedAt,

            Items = order.Items
                .Select(item => new OrderItemResponseDto
                {
                    ProductId = item.ProductId,

                    Quantity = item.Quantity,

                    UnitPrice = item.UnitPrice,

                    TotalPrice = item.TotalPrice
                })
                .ToList()
        };
    }
}