using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Models;

namespace OrderService.Repositories;

public class OrderRepository
    : Repository<Order>, IOrderRepository
{
    public OrderRepository(OrderDbContext context)
        : base(context)
    {
    }

    public async Task<Order?> GetOrderWithItemsAsync(
        Guid orderId)
    {
        return await _dbSet
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<IEnumerable<Order>> GetOrdersByUserIdAsync(
        Guid userId)
    {
        return await _dbSet
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }
}