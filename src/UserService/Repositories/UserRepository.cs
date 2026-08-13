using Microsoft.EntityFrameworkCore;
using UserService.Data;
using UserService.Models;

namespace UserService.Repositories;

public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(UserDbContext context)
        : base(context)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> ExistsByEmailAsync(string email)
    {
        return await _dbSet
            .AnyAsync(u => u.Email == email);
    }
}