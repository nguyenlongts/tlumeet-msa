namespace AuthService.Domain.Interfaces;

using AuthService.Domain.Models;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<User> CreateAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(int id);

    Task<bool> IsAccountLockedAsync(User user);
    Task IncrementFailedLoginAttemptsAsync(User user);
    Task ResetFailedLoginAttemptsAsync(User user);
    Task SaveResetTokenAsync(PasswordResetToken token);
    Task<PasswordResetToken?> GetResetTokenAsync(string token);
    Task UpdateResetTokenAsync(PasswordResetToken token);
    Task DeleteUnusedResetTokensAsync(int userId);

    Task SaveRefreshTokenAsync(RefreshToken refreshToken);
    Task <RefreshToken> GetRefreshTokenAsync(int userId);
    Task RevokeRefreshTokenAsync(string token);
    Task RevokeAllRefreshTokensAsync(int userId);
}