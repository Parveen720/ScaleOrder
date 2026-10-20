using System.Text.Json;
using OrderService.DTOs;
using StackExchange.Redis;

namespace OrderService.Services;

public class OrderCacheService : IOrderCacheService
{
    private readonly IDatabase _database;
    private readonly ILogger<OrderCacheService> _logger;

    private static readonly TimeSpan CacheDuration =
        TimeSpan.FromSeconds(60);

    public OrderCacheService(
        IConnectionMultiplexer redis,
        ILogger<OrderCacheService> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    private static string GetKey(Guid userId)
        => $"orders:user:{userId}";

    public async Task<List<OrderResponseDto>?> GetAsync(
        Guid userId)
    {
        try
        {
            var value = await _database.StringGetAsync(
                GetKey(userId));

            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<List<OrderResponseDto>>(
                value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Redis cache read failed for user {UserId}",
                userId);

            return null;
        }
    }

    public async Task SetAsync(
        Guid userId,
        List<OrderResponseDto> orders)
    {
        try
        {
            var json = JsonSerializer.Serialize(orders);

            await _database.StringSetAsync(
                GetKey(userId),
                json,
                CacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Redis cache write failed for user {UserId}",
                userId);
        }
    }

    public async Task InvalidateAsync(Guid userId)
    {
        try
        {
            await _database.KeyDeleteAsync(GetKey(userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Redis cache invalidation failed for user {UserId}",
                userId);
        }
    }
}