
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;

namespace PaymentService.Repositories;

public class PaymentRepository
    : Repository<Payment>, IPaymentRepository
{
    public PaymentRepository(PaymentDbContext context)
        : base(context)
    {
    }

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId)
    {
        return await _dbSet
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Payment>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Payment?> GetByIdempotencyKeyAsync(
        Guid userId,
        string idempotencyKey)
    {
        return await _dbSet.FirstOrDefaultAsync(
            p => p.UserId == userId &&
                 p.IdempotencyKey == idempotencyKey);
    }
}
