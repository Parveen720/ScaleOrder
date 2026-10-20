
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Npgsql;
using OrderService.Data;
using OrderService.DTOs;
using OrderService.Exceptions;
using OrderService.Models;
using OrderService.Repositories;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderCacheService _orderCache;

    public OrderService(
        IOrderRepository orderRepository,
        IIdempotencyRepository idempotencyRepository,
        OrderDbContext dbContext,
        ILogger<OrderService> logger,
        IOrderCacheService orderCache)
    {
        _orderRepository = orderRepository;
        _idempotencyRepository = idempotencyRepository;
        _dbContext = dbContext;
        _logger = logger;
        _orderCache = orderCache;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(
        Guid userId,
        CreateOrderRequestDto request,
        string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency-Key header is required.");
        }

        idempotencyKey = idempotencyKey.Trim();

        if (idempotencyKey.Length > 200)
        {
            throw new ArgumentException(
                "Idempotency-Key cannot exceed 200 characters.");
        }

        if (request?.Items == null || request.Items.Count == 0)
        {
            throw new ArgumentException(
                "Order must contain at least one item.");
        }

        foreach (var item in request.Items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new ArgumentException(
                    "ProductId cannot be empty.");
            }

            if (item.Quantity <= 0)
            {
                throw new ArgumentException(
                    "Quantity must be greater than zero.");
            }

            if (item.UnitPrice <= 0)
            {
                throw new ArgumentException(
                    "UnitPrice must be greater than zero.");
            }
        }

        var requestHash = GenerateRequestHash(request);

        var existingRecord = await _idempotencyRepository.GetAsync(
            userId,
            idempotencyKey);

        if (existingRecord != null)
        {
            return ReplayOrConflict(existingRecord, requestHash);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            foreach (var item in request.Items)
            {
                var totalPrice = item.Quantity * item.UnitPrice;

                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = totalPrice
                });
            }

            order.TotalAmount = order.Items.Sum(
                item => item.TotalPrice);
        }
        catch (OverflowException)
        {
            throw new ArgumentException(
                "Order amount exceeds the supported range.");
        }

        var response = MapToResponse(order);

        var idempotencyRecord = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = idempotencyKey,
            UserId = userId,
            RequestHash = requestHash,
            OrderId = order.Id,
            ResponseStatusCode = StatusCodes.Status201Created,
            ResponseBody = JsonSerializer.Serialize(response),
            CreatedAt = DateTime.UtcNow
        };

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        try
        {
            await _orderRepository.AddAsync(order);
            await _orderRepository.SaveChangesAsync();

            await _idempotencyRepository.AddAsync(idempotencyRecord);
            await _orderRepository.SaveChangesAsync();

            await transaction.CommitAsync();

            // Invalidate cached order list after successful commit.
            await _orderCache.InvalidateAsync(userId);

            _logger.LogInformation(
                "Order created. OrderId: {OrderId}, UserId: {UserId}",
                order.Id,
                userId);

            return response;
        }
        catch (DbUpdateException ex)
            when (IsIdempotencyUniqueViolation(ex))
        {
            await transaction.RollbackAsync();
            _dbContext.ChangeTracker.Clear();

            var winningRecord = await _idempotencyRepository.GetAsync(
                userId,
                idempotencyKey);

            if (winningRecord != null)
            {
                _logger.LogInformation(
                    "Concurrent idempotency request recovered. " +
                    "UserId: {UserId}, OrderId: {OrderId}",
                    userId,
                    winningRecord.OrderId);

                return ReplayOrConflict(winningRecord, requestHash);
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync();
            _dbContext.ChangeTracker.Clear();

            _logger.LogError(
                "Order transaction failed. UserId: {UserId}, Key: {Key}",
                userId,
                idempotencyKey);

            throw;
        }
    }

    private static bool IsIdempotencyUniqueViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState ==
                PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName ==
                "IX_IdempotencyRecords_UserId_Key";
    }

    private static OrderResponseDto ReplayOrConflict(
        IdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
            record.RequestHash,
            requestHash,
            StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        var previousResponse =
            JsonSerializer.Deserialize<OrderResponseDto>(
                record.ResponseBody);

        return previousResponse
            ?? throw new InvalidOperationException(
                "Stored idempotency response could not be read.");
    }

    public async Task<OrderResponseDto?> GetOrderAsync(Guid orderId)
    {
        var order = await _orderRepository.GetOrderWithItemsAsync(orderId);

        return order == null ? null : MapToResponse(order);
    }

    public async Task<IEnumerable<OrderResponseDto>> GetUserOrdersAsync(
        Guid userId)
    {
        var cachedOrders = await _orderCache.GetAsync(userId);

        if (cachedOrders != null)
        {
            _logger.LogInformation(
                "Order cache hit for user {UserId}",
                userId);

            return cachedOrders;
        }

        _logger.LogInformation(
            "Order cache miss for user {UserId}",
            userId);

        var orders = await _orderRepository.GetOrdersByUserIdAsync(
            userId);

        var result = orders.Select(MapToResponse).ToList();

        await _orderCache.SetAsync(userId, result);

        return result;
    }

    private static string GenerateRequestHash(
        CreateOrderRequestDto request)
    {
        var json = JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    private static OrderResponseDto MapToResponse(Order order)
    {
        return new OrderResponseDto
        {
            Id = order.Id,
            UserId = order.UserId,
            TotalAmount = order.TotalAmount,
            Status = order.Status.ToString(),
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(item => new OrderItemResponseDto
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                TotalPrice = item.TotalPrice
            }).ToList()
        };
    }

    public async Task<bool> MarkOrderAsPaidAsync(Guid orderId)
    {
        var order = await _orderRepository.GetOrderWithItemsAsync(orderId);

        if (order == null)
            return false;

        if (order.Status == OrderStatus.Cancelled)
            return false;

        if (order.Status == OrderStatus.Paid ||
            order.Status == OrderStatus.Processing ||
            order.Status == OrderStatus.Shipped ||
            order.Status == OrderStatus.Delivered)
        {
            return true;
        }

        order.Status = OrderStatus.Paid;
        order.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        // Invalidate the user's cached order list.
        await _orderCache.InvalidateAsync(order.UserId);

        _logger.LogInformation(
            "Order marked as paid. OrderId: {OrderId}",
            orderId);

        return true;
    }
}

