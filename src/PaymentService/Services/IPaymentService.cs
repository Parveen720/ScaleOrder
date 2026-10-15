
using PaymentService.DTOs;
using PaymentService.Models;

namespace PaymentService.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> CreatePaymentAsync(
        Guid userId,
        CreatePaymentRequest request,
        string accessToken,
        string idempotencyKey);

    Task<Payment?> GetPaymentByIdAsync(Guid paymentId);

    Task<IEnumerable<PaymentResponseDto>> GetPaymentsByUserIdAsync(
        Guid userId);
}
