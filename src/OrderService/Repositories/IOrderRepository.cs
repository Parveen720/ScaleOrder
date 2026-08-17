using OrderService.Models;

namespace OrderService.Repositories;

public interface IOrderRepository : IRepository<Order>
{
    Task<Order?> GetOrderWithItemsAsync(Guid orderId);

    Task<IEnumerable<Order>> GetOrdersByUserIdAsync(Guid userId);
}