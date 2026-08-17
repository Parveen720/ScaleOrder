using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using OrderService.Data;
using OrderService.DTOs;
using OrderService.Models;
using OrderService.Repositories;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly OrderDbContext _dbContext;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IIdempotencyRepository idempotencyRepository,
        OrderDbContext dbContext,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _idempotencyRepository = idempotencyRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(
        Guid userId,
        CreateOrderRequestDto request,
        string idempotencyKey)
    {
        // --------------------------------------------------
        // 1. Validate Idempotency Key
        // --------------------------------------------------

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

        // --------------------------------------------------
        // 2. Check whether this request was already processed
        // --------------------------------------------------

        var existingRecord =
            await _idempotencyRepository.GetAsync(
                userId,
                idempotencyKey);

        if (existingRecord != null)
        {
            _logger.LogInformation(
                "Duplicate order request detected. UserId: {UserId}, IdempotencyKey: {IdempotencyKey}, OrderId: {OrderId}",
                userId,
                idempotencyKey,
                existingRecord.OrderId);

            var previousResponse =
                JsonSerializer.Deserialize<OrderResponseDto>(
                    existingRecord.ResponseBody);

            if (previousResponse == null)
            {
                throw new InvalidOperationException(
                    "Stored idempotency response could not be read.");
            }

            return previousResponse;
        }

        // --------------------------------------------------
        // 3. Validate Order
        // --------------------------------------------------

        if (request.Items == null ||
            request.Items.Count == 0)
        {
            throw new ArgumentException(
                "Order must contain at least one item.");
        }

        // --------------------------------------------------
        // 4. Generate request hash
        // --------------------------------------------------

        var requestHash =
            GenerateRequestHash(request);

        // --------------------------------------------------
        // 5. Create Order
        // --------------------------------------------------

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

        // --------------------------------------------------
        // 6. Create response
        // --------------------------------------------------

        var response = MapToResponse(order);

        var responseBody =
            JsonSerializer.Serialize(response);

        // --------------------------------------------------
        // 7. Create Idempotency Record
        // --------------------------------------------------

        var idempotencyRecord = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),

            Key = idempotencyKey,

            UserId = userId,

            RequestHash = requestHash,

            OrderId = order.Id,

            ResponseStatusCode = 201,

            ResponseBody = responseBody,

            CreatedAt = DateTime.UtcNow
        };

        // --------------------------------------------------
        // 8. ATOMIC TRANSACTION
        // --------------------------------------------------

        await using var transaction =
            await _dbContext.Database
                .BeginTransactionAsync();

        try
        {
            // Save Order
            await _orderRepository.AddAsync(order);

            await _orderRepository.SaveChangesAsync();

            // Save Idempotency Record
            await _idempotencyRepository
                .AddAsync(idempotencyRecord);

            await _orderRepository
                .SaveChangesAsync();

            // Both operations succeeded
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Order created successfully. OrderId: {OrderId}, UserId: {UserId}, TotalAmount: {TotalAmount}",
                order.Id,
                userId,
                order.TotalAmount);

            return response;
        }
        catch
        {
            // Order and IdempotencyRecord are both rolled back
            await transaction.RollbackAsync();

            _logger.LogError(
                "Failed to create order transaction. UserId: {UserId}, IdempotencyKey: {IdempotencyKey}",
                userId,
                idempotencyKey);

            throw;
        }
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

    private static string GenerateRequestHash(
        CreateOrderRequestDto request)
    {
        var json = JsonSerializer.Serialize(request);

        var bytes = Encoding.UTF8.GetBytes(json);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
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