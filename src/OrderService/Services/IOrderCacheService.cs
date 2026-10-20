using OrderService.DTOs;

namespace OrderService.Services;

public interface IOrderCacheService
{
    Task<List<OrderResponseDto>?> GetAsync(Guid userId);

    Task SetAsync(
        Guid userId,
        List<OrderResponseDto> orders);

    Task InvalidateAsync(Guid userId);
}