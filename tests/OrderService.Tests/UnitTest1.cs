
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OrderService.Data;
using OrderService.DTOs;
using OrderService.Exceptions;
using OrderService.Models;
using OrderService.Repositories;
using OrderService.Services;
using Xunit;

namespace OrderService.Tests;

public class UnitTest1
{
    private static async Task<(
        SqliteConnection Connection,
        OrderDbContext Db,
        OrderService.Services.OrderService Service,
        Mock<IOrderCacheService> Cache)>
        CreateServiceAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new OrderDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var orderRepository = new OrderRepository(db);
        var idempotencyRepository = new IdempotencyRepository(db);

        var logger = Mock.Of<ILogger<OrderService.Services.OrderService>>();
        var cache = new Mock<IOrderCacheService>();

        cache.Setup(c => c.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync((List<OrderResponseDto>?)null);

        var service = new OrderService.Services.OrderService(
            orderRepository,
            idempotencyRepository,
            db,
            logger,
            cache.Object);

        return (connection, db, service, cache);
    }

    private static CreateOrderRequestDto CreateRequest()
    {
        return new CreateOrderRequestDto
        {
            Items =
            [
                new()
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1,
                    UnitPrice = 30
                }
            ]
        };
    }

    [Fact]
    public async Task SameKeyAndSameRequest_ShouldReturnExistingOrder()
    {
        var (connection, db, service, _) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();
            var key = Guid.NewGuid().ToString();
            var request = CreateRequest();

            var first = await service.CreateOrderAsync(userId, request, key);
            var second = await service.CreateOrderAsync(userId, request, key);

            Assert.Equal(first.Id, second.Id);
            Assert.Single(await db.Orders.ToListAsync());
            Assert.Single(await db.IdempotencyRecords.ToListAsync());
        }
    }

    [Fact]
    public async Task SameKeyAndDifferentRequest_ShouldThrowConflict()
    {
        var (connection, db, service, _) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();
            var key = Guid.NewGuid().ToString();

            await service.CreateOrderAsync(userId, CreateRequest(), key);

            await Assert.ThrowsAsync<IdempotencyConflictException>(
                () => service.CreateOrderAsync(
                    userId,
                    new CreateOrderRequestDto
                    {
                        Items =
                        [
                            new()
                            {
                                ProductId = Guid.NewGuid(),
                                Quantity = 2,
                                UnitPrice = 50
                            }
                        ]
                    },
                    key));

            Assert.Single(await db.Orders.ToListAsync());
        }
    }

    [Fact]
    public async Task DifferentKeys_ShouldCreateDifferentOrders()
    {
        var (connection, db, service, _) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();
            var request = CreateRequest();

            var first = await service.CreateOrderAsync(
                userId, request, Guid.NewGuid().ToString());

            var second = await service.CreateOrderAsync(
                userId, request, Guid.NewGuid().ToString());

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(2, await db.Orders.CountAsync());
        }
    }

    [Fact]
    public async Task GetUserOrders_CacheHit_ShouldReturnCachedOrders()
    {
        var (connection, db, service, cache) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();
            var cachedOrders = new List<OrderResponseDto>();

            cache.Setup(c => c.GetAsync(userId))
                .ReturnsAsync(cachedOrders);

            var result = await service.GetUserOrdersAsync(userId);

            Assert.Same(cachedOrders, result);
            cache.Verify(c => c.SetAsync(
                It.IsAny<Guid>(),
                It.IsAny<List<OrderResponseDto>>()), Times.Never);
        }
    }

    [Fact]
    public async Task GetUserOrders_CacheMiss_ShouldStoreOrdersInCache()
    {
        var (connection, db, service, cache) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();

            var result = await service.GetUserOrdersAsync(userId);

            Assert.Empty(result);

            cache.Verify(c => c.SetAsync(
                userId,
                It.Is<List<OrderResponseDto>>(orders => orders.Count == 0)),
                Times.Once);
        }
    }

    [Fact]
    public async Task CreateOrder_ShouldInvalidateUserCache()
    {
        var (connection, db, service, cache) = await CreateServiceAsync();

        await using (connection)
        await using (db)
        {
            var userId = Guid.NewGuid();

            await service.CreateOrderAsync(
                userId,
                CreateRequest(),
                Guid.NewGuid().ToString());

            cache.Verify(c => c.InvalidateAsync(userId), Times.Once);
        }
    }
}
