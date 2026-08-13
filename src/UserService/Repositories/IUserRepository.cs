using UserService.Models;

namespace UserService.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);

    Task<bool> ExistsByEmailAsync(string email);
}