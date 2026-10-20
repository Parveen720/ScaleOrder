
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentService.Data;
using PaymentService.DTOs;
using PaymentService.Models;
using PaymentService.Repositories;
using Xunit;

namespace PaymentService.Tests;

public class UnitTest1
{
    private static async Task<(
        SqliteConnection Connection,
        PaymentDbContext Db,
        PaymentService.Services.PaymentService Service)>
        CreateServiceAsync(Guid expectedUserId)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new PaymentDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var repository = new PaymentRepository(db);

        var handler = new FakeOrderServiceHandler(expectedUserId);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:OrderServiceUrl"] = "http://localhost:5078/",
                ["InternalServices:ServiceSecret"] = "test-secret"
            })
            .Build();

        var logger =
            Mock.Of<ILogger<PaymentService.Services.PaymentService>>();

        var service = new PaymentService.Services.PaymentService(
            repository,
            db,
            httpClientFactory.Object,
            configuration,
            logger);

        return (connection, db, service);
    }

    private static CreatePaymentRequest CreateRequest(
        Guid orderId,
        bool simulateFailure = false)
    {
        return new CreatePaymentRequest
        {
            OrderId = orderId,
            SimulateFailure = simulateFailure
        };
    }

    [Fact]
    public async Task SameKeyAndSameOrder_ShouldReturnExistingPayment()
    {
        var userId = Guid.NewGuid();
        var (connection, db, service) = await CreateServiceAsync(userId);

        await using (connection)
        await using (db)
        {
            var orderId = Guid.NewGuid();
            var key = Guid.NewGuid().ToString();
            var request = CreateRequest(orderId);

            var first = await service.CreatePaymentAsync(
                userId, request, "test-token", key);

            var second = await service.CreatePaymentAsync(
                userId, request, "test-token", key);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal("Succeeded", second.Status);
            Assert.Equal(1, await db.Payments.CountAsync());
            Assert.Equal(1, await db.OutboxMessages.CountAsync());
        }
    }

    [Fact]
    public async Task SameKeyAndDifferentOrder_ShouldThrowConflict()
    {
        var userId = Guid.NewGuid();
        var (connection, db, service) = await CreateServiceAsync(userId);

        await using (connection)
        await using (db)
        {
            var key = Guid.NewGuid().ToString();

            await service.CreatePaymentAsync(
                userId,
                CreateRequest(Guid.NewGuid()),
                "test-token",
                key);

            await Assert.ThrowsAsync<
                PaymentService.Services.IdempotencyConflictException>(
                () => service.CreatePaymentAsync(
                    userId,
                    CreateRequest(Guid.NewGuid()),
                    "test-token",
                    key));

            Assert.Equal(1, await db.Payments.CountAsync());
            Assert.Equal(1, await db.OutboxMessages.CountAsync());
        }
    }

    [Fact]
    public async Task FailedPayment_ShouldSaveFailedStatusAndEvent()
    {
        var userId = Guid.NewGuid();
        var (connection, db, service) = await CreateServiceAsync(userId);

        await using (connection)
        await using (db)
        {
            var response = await service.CreatePaymentAsync(
                userId,
                CreateRequest(Guid.NewGuid(), simulateFailure: true),
                "test-token",
                Guid.NewGuid().ToString());

            var payment = await db.Payments.SingleAsync();
            var outbox = await db.OutboxMessages.SingleAsync();

            Assert.Equal("Failed", response.Status);
            Assert.Equal(PaymentStatus.Failed, payment.Status);
            Assert.Equal("PaymentFailed", outbox.EventType);

            using var payload = JsonDocument.Parse(outbox.Payload);

            Assert.Equal(
                "PaymentFailed",
                payload.RootElement.GetProperty("EventType").GetString());
        }
    }

    [Fact]
    public async Task SuccessfulPayment_ShouldSaveSucceededStatusAndEvent()
    {
        var userId = Guid.NewGuid();
        var (connection, db, service) = await CreateServiceAsync(userId);

        await using (connection)
        await using (db)
        {
            var response = await service.CreatePaymentAsync(
                userId,
                CreateRequest(Guid.NewGuid()),
                "test-token",
                Guid.NewGuid().ToString());

            var payment = await db.Payments.SingleAsync();
            var outbox = await db.OutboxMessages.SingleAsync();

            Assert.Equal("Succeeded", response.Status);
            Assert.Equal(PaymentStatus.Succeeded, payment.Status);
            Assert.Equal("PaymentSucceeded", outbox.EventType);

            using var payload = JsonDocument.Parse(outbox.Payload);

            Assert.Equal(
                "PaymentSucceeded",
                payload.RootElement.GetProperty("EventType").GetString());
        }
    }

    private sealed class FakeOrderServiceHandler : HttpMessageHandler
    {
        private readonly Guid _expectedUserId;

        public FakeOrderServiceHandler(Guid expectedUserId)
        {
            _expectedUserId = expectedUserId;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (path.StartsWith(
                    "/api/orders/internal/",
                    StringComparison.OrdinalIgnoreCase))
            {
                var secretIsValid =
                    request.Headers.TryGetValues(
                        "X-Internal-Service-Secret",
                        out var values) &&
                    values.Contains("test-secret");

                return Task.FromResult(new HttpResponseMessage(
                    secretIsValid
                        ? HttpStatusCode.OK
                        : HttpStatusCode.Unauthorized));
            }

            if (request.Method == HttpMethod.Get &&
                path.StartsWith(
                    "/api/orders/",
                    StringComparison.OrdinalIgnoreCase))
            {
                var orderIdText = path["/api/orders/".Length..];

                if (!Guid.TryParse(orderIdText, out var orderId))
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var order = new
                {
                    Id = orderId,
                    UserId = _expectedUserId,
                    TotalAmount = 150.00m
                };

                return Task.FromResult(new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(order),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
