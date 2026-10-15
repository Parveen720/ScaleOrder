
using PaymentService.Models;

namespace PaymentService.Repositories;

public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetByOrderIdAsync(Guid orderId);

    Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId);

    Task<Payment?> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey);
}
