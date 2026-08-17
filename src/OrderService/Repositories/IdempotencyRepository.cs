using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;

namespace OrderService.Repositories;

public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly OrderDbContext _context;

    public IdempotencyRepository(OrderDbContext context)
    {
        _context = context;
    }

    public async Task<IdempotencyRecord?> GetAsync(
        Guid userId,
        string key)
    {
        return await _context.IdempotencyRecords
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.Key == key);
    }

    public async Task AddAsync(
        IdempotencyRecord record)
    {
        await _context.IdempotencyRecords.AddAsync(record);
    }
}
