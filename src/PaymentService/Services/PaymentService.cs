
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentService.DTOs;
using PaymentService.Models;
using PaymentService.Repositories;

namespace PaymentService.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _paymentRepository = paymentRepository;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
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

        // Retry handling: if payment already exists, retry the order update.
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

        // Verify that the order belongs to the authenticated user.
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _paymentRepository.AddAsync(payment);

        try
        {
            await _paymentRepository.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Another request may have inserted the same idempotency key.
            if (ex.InnerException is PostgresException postgresException &&
                postgresException.SqlState ==
                PostgresErrorCodes.UniqueViolation)
            {
                throw new IdempotencyConflictException(
                    "A payment with this Idempotency-Key was concurrently created. " +
                    "Retry the request using the same key.");
            }

            throw;
        }

        // Payment is persisted. Now update OrderService.
        // If this fails, retrying with the same key attempts this update again.
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
