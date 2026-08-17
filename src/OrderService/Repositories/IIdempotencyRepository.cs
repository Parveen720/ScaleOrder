using OrderService.Models;

namespace OrderService.Repositories;

public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> GetAsync(
        Guid userId,
        string key);

    Task AddAsync(
        IdempotencyRecord record);
}