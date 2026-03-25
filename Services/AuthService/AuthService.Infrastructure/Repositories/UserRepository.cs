using Microsoft.EntityFrameworkCore;
using AuthService.Domain.Interfaces;
using AuthService.Domain.Models;

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
        if (string.IsNullOrWhiteSpace(email)) return null;
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserInfo)
            .Include(u => u.LoginInfo)
            .FirstOrDefaultAsync(u => u.Email == email.ToLower());
    }

    public async Task<List<User>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        return await _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserInfo)
            .Include(u => u.LoginInfo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<User> CreateAsync(User user)
    {
        if (user.RoleId == null)
            user.RoleId = 2;

        user.UserInfo = new UserInfo();
        user.LoginInfo = new UserLogin();

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

    public async Task<bool> IsAccountLockedAsync(User user)
    {
        if (!user.IsActive) return true;

        if (user.LoginInfo?.AccountLockedUntil > DateTime.UtcNow) return true;

        if (user.LoginInfo?.AccountLockedUntil <= DateTime.UtcNow)
        {
            user.LoginInfo.AccountLockedUntil = null;
            user.LoginInfo.FailedLoginAttempts = 0;
            await UpdateAsync(user);
        }

        return false;
    }

    public async Task IncrementFailedLoginAttemptsAsync(User user)
    {
        if (user.LoginInfo == null) return;

        user.LoginInfo.FailedLoginAttempts++;
        if (user.LoginInfo.FailedLoginAttempts >= 5)
            user.LoginInfo.AccountLockedUntil = DateTime.UtcNow.AddMinutes(15);

        await UpdateAsync(user);
    }

    public async Task ResetFailedLoginAttemptsAsync(User user)
    {
        if (user.LoginInfo == null) return;

        user.LoginInfo.FailedLoginAttempts = 0;
        user.LoginInfo.AccountLockedUntil = null;
        await UpdateAsync(user);
    }

    public async Task SaveResetTokenAsync(PasswordResetToken token)
    {
        _context.PasswordResetTokens.Add(token);
        await _context.SaveChangesAsync();
    }

    public async Task<PasswordResetToken?> GetResetTokenAsync(string token)
    {
        return await _context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task UpdateResetTokenAsync(PasswordResetToken token)
    {
        _context.PasswordResetTokens.Update(token);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteUnusedResetTokensAsync(int userId)
    {
        var tokens = await _context.PasswordResetTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync();

        _context.PasswordResetTokens.RemoveRange(tokens);
        await _context.SaveChangesAsync();
    }

    public Task SaveRefreshTokenAsync(RefreshToken refreshToken)
    {
        throw new NotImplementedException();
    }

    public Task<RefreshToken> GetRefreshTokenAsync(int userId)
    {
        throw new NotImplementedException();
    }

    public Task RevokeRefreshTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    public Task RevokeAllRefreshTokensAsync(int userId)
    {
        throw new NotImplementedException();
    }
}