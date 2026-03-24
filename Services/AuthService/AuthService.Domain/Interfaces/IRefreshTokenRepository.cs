using AuthService.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthService.Domain.Interfaces
{
    public interface IRefreshTokenRepository
    {
        Task SaveAsync(RefreshToken refreshToken);
        Task<RefreshToken?> GetByTokenAsync(string token);
        Task<bool> RevokeAsync(string token);
        Task<bool> RevokeAllByUserIdAsync(int userId);
    }
}
