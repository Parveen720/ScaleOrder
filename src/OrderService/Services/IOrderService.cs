using OrderService.DTOs;

namespace OrderService.Services;

public interface IOrderService
{
    Task<OrderResponseDto> CreateOrderAsync(
        Guid userId,
        CreateOrderRequestDto request,
        string idempotencyKey);

    Task<OrderResponseDto?> GetOrderAsync(
        Guid orderId);

    Task<IEnumerable<OrderResponseDto>> GetUserOrdersAsync(
        Guid userId);
}
