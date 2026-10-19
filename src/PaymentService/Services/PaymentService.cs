
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.Data;
using PaymentService.DTOs;
using PaymentService.Models;
using PaymentService.Repositories;

namespace PaymentService.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly PaymentDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        PaymentDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PaymentResponseDto> CreatePaymentAsync(
        Guid userId,
        CreatePaymentRequest request,
        string accessToken,
        string idempotencyKey)
    {
        if (userId == Guid.Empty)
            throw new InvalidOperationException("Invalid user.");

        if (request == null || request.OrderId == Guid.Empty)
            throw new InvalidOperationException(
                "A valid OrderId is required.");

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 128)
        {
            throw new InvalidOperationException(
                "A valid Idempotency-Key is required.");
        }

        idempotencyKey = idempotencyKey.Trim();

        var fingerprint = ComputeFingerprint(request.OrderId);

        var existing = await _paymentRepository.GetByIdempotencyKeyAsync(
            userId, idempotencyKey);

        if (existing != null)
        {
            if (existing.RequestFingerprint != fingerprint)
            {
                throw new IdempotencyConflictException(
                    "This Idempotency-Key was already used with a different request.");
            }

            if (existing.Status == PaymentStatus.Succeeded)
                await EnsureOrderMarkedPaidAsync(existing.OrderId);

            return ToResponse(existing);
        }

        var orderServiceUrl =
            _configuration["Services:OrderServiceUrl"]
            ?? "http://localhost:5078/";

        if (!Uri.TryCreate(
                orderServiceUrl,
                UriKind.Absolute,
                out var orderServiceUri))
        {
            throw new InvalidOperationException(
                "OrderService URL is invalid.");
        }

        using var client = _httpClientFactory.CreateClient();

        client.BaseAddress = orderServiceUri;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync(
            $"api/orders/{request.OrderId}");

        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "OrderService rejected the authorization token.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Order was not found or does not belong to this user.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OrderService returned HTTP {(int)response.StatusCode}.");
        }

        var order = await response.Content
            .ReadFromJsonAsync<OrderVerificationResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (order == null ||
            order.Id != request.OrderId ||
            order.UserId != userId ||
            order.TotalAmount <= 0)
        {
            throw new InvalidOperationException(
                "Order details are invalid or do not belong to this user.");
        }

        var now = DateTime.UtcNow;

        var payment = new Payment
        {
            OrderId = order.Id,
            UserId = userId,
            Amount = order.TotalAmount,
            Currency = "INR",
            Status = PaymentStatus.Succeeded,
            Provider = "Simulated",
            ProviderReference = $"SIM-{Guid.NewGuid():N}",
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            CreatedAt = now,
            UpdatedAt = now
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "PaymentSucceeded",
            Payload = JsonSerializer.Serialize(new
            {
                EventId = Guid.NewGuid(),
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                UserId = payment.UserId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                OccurredAt = now
            }),
            OccurredAt = now,
            RetryCount = 0
        };

        // Persist payment and its event atomically.
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        try
        {
            await _paymentRepository.AddAsync(payment);
            _dbContext.OutboxMessages.Add(outboxMessage);

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _dbContext.ChangeTracker.Clear();

            if (ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState ==
                PostgresErrorCodes.UniqueViolation)
            {
                throw new IdempotencyConflictException(
                    "A payment with this Idempotency-Key may already exist. " +
                    "Retry using the same key.");
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync();
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _logger.LogInformation(
            "Payment and outbox event saved. PaymentId: {PaymentId}, " +
            "OrderId: {OrderId}, EventId: {EventId}",
            payment.Id,
            payment.OrderId,
            outboxMessage.Id);

        // Temporary compatibility path: preserve the working UI flow.
        // Kafka will replace this direct notification in a later step.
        await EnsureOrderMarkedPaidAsync(payment.OrderId);

        return ToResponse(payment);
    }

    private async Task EnsureOrderMarkedPaidAsync(Guid orderId)
    {
        var orderServiceUrl =
            _configuration["Services:OrderServiceUrl"]
            ?? "http://localhost:5078/";

        if (!Uri.TryCreate(
                orderServiceUrl,
                UriKind.Absolute,
                out var baseUri))
        {
            throw new InvalidOperationException(
                "OrderService URL is invalid.");
        }

        var secret =
            _configuration["InternalServices:ServiceSecret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "Internal service secret is not configured.");
        }

        using var client = _httpClientFactory.CreateClient();

        var endpoint =
            $"{baseUri.ToString().TrimEnd('/')}/api/orders/internal/{orderId}/paid";

        using var request = new HttpRequestMessage(
            HttpMethod.Post, endpoint);

        request.Headers.Add("X-Internal-Service-Secret", secret);

        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Payment exists, but OrderService could not mark order " +
                $"{orderId} as paid. HTTP {(int)response.StatusCode}.");
        }
    }

    public async Task<Payment?> GetPaymentByIdAsync(Guid paymentId)
    {
        return await _paymentRepository.GetByIdAsync(paymentId);
    }

    public async Task<IEnumerable<PaymentResponseDto>>
        GetPaymentsByUserIdAsync(Guid userId)
    {
        var payments = await _paymentRepository.GetByUserIdAsync(userId);

        return payments.Select(ToResponse).ToList();
    }

    private static string ComputeFingerprint(Guid orderId)
    {
        var input = orderId.ToString("D");
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    private static PaymentResponseDto ToResponse(Payment payment)
    {
        return new PaymentResponseDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            Provider = payment.Provider,
            ProviderReference = payment.ProviderReference,
            CreatedAt = payment.CreatedAt
        };
    }

    private sealed class OrderVerificationResponse
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public decimal TotalAmount { get; set; }
    }
}
