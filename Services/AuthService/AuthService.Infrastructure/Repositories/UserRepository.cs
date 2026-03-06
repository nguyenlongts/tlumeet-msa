using Microsoft.EntityFrameworkCore;
using AuthService.Domain.Interfaces;
using AuthService.Domain.Models;
using AuthService.Infrastructure.Data;

namespace AuthService.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AuthDbContext _context;

    public UserRepository(AuthDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserInfo)
            .Include(u => u.LoginInfo)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserInfo)
            .Include(u => u.LoginInfo)
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserInfo)
            .Include(u => u.LoginInfo)
            .ToListAsync();
    }

    public async Task<User> CreateAsync(User user)
    {
        if (user.RoleId == null)
        {
            user.RoleId = 2; 
        }

        user.UserInfo = new UserInfo { UserId = user.Id };
        user.LoginInfo = new UserLogin { UserId = user.Id };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(user.Id) ?? user;
    }

    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> IsAccountLockedAsync(string email)
    {
        var user = await GetByEmailAsync(email);
        if (user == null) return false;

        if (!user.IsActive) return true;

        if (user.LoginInfo?.AccountLockedUntil.HasValue == true)
        {
            if (user.LoginInfo.AccountLockedUntil.Value > DateTime.UtcNow)
            {
                return true;
            }
            else
            {
                user.LoginInfo.AccountLockedUntil = null;
                user.LoginInfo.FailedLoginAttempts = 0;
                await UpdateAsync(user);
            }
        }

        return false;
    }

    public async Task IncrementFailedLoginAttemptsAsync(string email)
    {
        var user = await GetByEmailAsync(email);
        if (user?.LoginInfo == null) return;

        user.LoginInfo.FailedLoginAttempts++;

        if (user.LoginInfo.FailedLoginAttempts >= 5)
        {
            user.LoginInfo.AccountLockedUntil = DateTime.UtcNow.AddMinutes(15);
        }

        await UpdateAsync(user);
    }

    public async Task ResetFailedLoginAttemptsAsync(string email)
    {
        var user = await GetByEmailAsync(email);
        if (user?.LoginInfo == null) return;

        user.LoginInfo.FailedLoginAttempts = 0;
        user.LoginInfo.AccountLockedUntil = null;
        await UpdateAsync(user);
    }
}