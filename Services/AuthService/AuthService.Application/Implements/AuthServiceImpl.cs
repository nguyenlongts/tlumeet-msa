using AuthService.Application.DTOs;
using AuthService.Application.Interfaces;
using AuthService.Domain.Interfaces;
using AuthService.Domain.Models;
using AuthService.Infrastructure;
using AuthService.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuthService.Application.Services.Implements;

public class AuthServiceImpl : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IConfiguration _configuration;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly AuthDbContext _context;
    private readonly ILogger<AuthServiceImpl> _logger;
    public AuthServiceImpl(IUserRepository userRepository, IConfiguration configuration, IKafkaProducer kafkaProducer, ILogger<AuthServiceImpl> logger, IRefreshTokenRepository refreshTokenRepository, AuthDbContext context)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
        _refreshTokenRepository = refreshTokenRepository;
        _context = context;
    }

    public async Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Tên không được để trống");

            if (string.IsNullOrWhiteSpace(request.Email))
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Email không được để trống");

            if (!IsValidEmail(request.Email))
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Email không hợp lệ");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Mật khẩu phải có ít nhất 6 ký tự");

            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser != null)
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Email đã được sử dụng");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                UserName = request.Name,
                Email = request.Email.ToLower(),
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
           
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var createdUser = await _userRepository.CreateAsync(user);

                var refreshToken = CreateNewRefreshToken(user.Id);
                await _context.RefreshTokens.AddAsync(refreshToken);
                var outboxMessage = new OutboxMessage
                {
                    EventType = nameof(UserRegisteredEvent),
                    Payload = JsonSerializer.Serialize(new UserRegisteredEvent
                    {
                        UserId = createdUser.Id,
                        Email = createdUser.Email,
                        UserName = createdUser.UserName,
                        RegisteredAt = DateTime.UtcNow,
                    }),
                    CreatedAt = DateTime.UtcNow,
                    ErrorMessage = null
                };
                
                await _context.OutboxMessages.AddAsync(outboxMessage);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                var accessToken = GenerateJwtToken(createdUser);

                var response = new AuthResponse
                {
                    Id = createdUser.Id,
                    Name = createdUser.UserName,
                    Email = createdUser.Email,
                    Token = accessToken,
                    RefreshToken = refreshToken.Token.ToString()
                };
                return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng ký thành công");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi transaction RegisterAsync");
                return ApiResponse<AuthResponse>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error RegisterAsync");
            return ApiResponse<AuthResponse>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Email không được để trống");

            if (string.IsNullOrWhiteSpace(request.Password))
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Mật khẩu không được để trống");

            var user = await _userRepository.GetByEmailAsync(request.Email.ToLower());
            if (user == null)
                return ApiResponse<AuthResponse>.ErrorResponse(401, "Email hoặc mật khẩu không chính xác");

            var isLocked = await _userRepository.IsAccountLockedAsync(user);
            if (isLocked)
            {
                if (!user.IsActive)
                    return ApiResponse<AuthResponse>.ErrorResponse(403, "Tài khoản đã bị vô hiệu hóa");

                if (user.LoginInfo?.AccountLockedUntil.HasValue == true)
                {
                    var remainingMinutes = (int)Math.Ceiling(
                        (user.LoginInfo.AccountLockedUntil.Value - DateTime.UtcNow).TotalMinutes);
                    return ApiResponse<AuthResponse>.ErrorResponse(403,
                        $"Tài khoản bị khóa. Thử lại sau {remainingMinutes} phút");
                }
            }

            var isValidPassword = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            if (!isValidPassword)
            {
                await _userRepository.IncrementFailedLoginAttemptsAsync(user);

                var isLockedAfterFail = await _userRepository.IsAccountLockedAsync(user);
                if (isLockedAfterFail)
                {
                    return ApiResponse<AuthResponse>.ErrorResponse(403,
                        "Tài khoản đã bị khóa do đăng nhập sai quá nhiều lần");
                }

                return ApiResponse<AuthResponse>.ErrorResponse(401, "Email hoặc mật khẩu không chính xác");
            }

            if (user.LoginInfo != null)
            {
                user.LoginInfo.FailedLoginAttempts = 0;
                user.LoginInfo.AccountLockedUntil = null;
                user.LoginInfo.LastLogin = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }

            var token = GenerateJwtToken(user);

            var refreshToken = Guid.NewGuid();
            await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);
            var refreshTokenExpDays = int.Parse(_configuration["Jwt:RefreshTokenExpiration"] ?? "7");

            await _refreshTokenRepository.SaveAsync(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiredAt = DateTime.UtcNow.AddDays(refreshTokenExpDays),
                CreatedAt = DateTime.UtcNow,
                RevokeAt = null
            });
            var response = new AuthResponse
            {
                Id = user.Id,
                Name = user.UserName,
                Email = user.Email,
                Token = token,
                RefreshToken = refreshToken.ToString()
            };

            return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng nhập thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi login");
            return ApiResponse<AuthResponse>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }

    public async Task<ApiResponse<bool>> ChangePasswordAsync(string userEmail, ChangePasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                return ApiResponse<bool>.ErrorResponse(400, "Mật khẩu hiện tại không được để trống");

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                return ApiResponse<bool>.ErrorResponse(400, "Mật khẩu mới phải có ít nhất 6 ký tự");

            var user = await _userRepository.GetByEmailAsync(userEmail);
            if (user == null)
                return ApiResponse<bool>.ErrorResponse(404, "Người dùng không tồn tại");

            var isValid = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash);
            if (!isValid)
                return ApiResponse<bool>.ErrorResponse(400, "Mật khẩu hiện tại không đúng");
            if (request.CurrentPassword == request.NewPassword)
                return ApiResponse<bool>.ErrorResponse(400, "Mật khẩu mới không được trùng mật khẩu cũ");
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);
            await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id);

            return ApiResponse<bool>.SuccessResponse(true, "Đổi mật khẩu thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Change password");
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }

    public async Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return ApiResponse<bool>.ErrorResponse(400, "Email không được để trống");

            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null)
                return ApiResponse<bool>.SuccessResponse(true, "Nếu email tồn tại, link reset đã được gửi");
            await _userRepository.DeleteUnusedResetTokensAsync(user.Id);

            var resetToken = GenerateResetPasswordToken(user);

            var passwordResetEvent = new PasswordResetRequestedEvent
            {
                Email = request.Email,
                ResetToken = resetToken,
                RequestedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };

            try
            {
                await _kafkaProducer.PublishAsync(KafkaTopics.PasswordResetRequested, passwordResetEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PasswordReset event for {Email}", request.Email);
                return ApiResponse<bool>.ErrorResponse(503, "Không thể gửi email lúc này, vui lòng thử lại sau");
            }
            await _userRepository.SaveResetTokenAsync(new PasswordResetToken
            {
                UserId = user.Id,
                Token = resetToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            await _userRepository.ResetFailedLoginAttemptsAsync(user);

            return ApiResponse<bool>.SuccessResponse(true, "Đã gửi email đặt lại mật khẩu");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Forgot Password");

            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }

    public async Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return ApiResponse<bool>.ErrorResponse(400, "Token không hợp lệ");

            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                return ApiResponse<bool>.ErrorResponse(400, "Mật khẩu mới tối thiểu 6 ký tự");

            var handler = new JwtSecurityTokenHandler();
            JwtSecurityToken jwt;

            try
            {
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true
                };
                handler.ValidateToken(request.Token, validationParams, out var validatedToken);
                jwt = (JwtSecurityToken)validatedToken;
            }
            catch
            {
                return ApiResponse<bool>.ErrorResponse(400, "Token không hợp lệ");
            }

            var type = jwt.Claims.FirstOrDefault(c => c.Type == "type")?.Value;
            if (type != "reset-password")
                return ApiResponse<bool>.ErrorResponse(400, "Token không hợp lệ");
            var savedToken = await _userRepository.GetResetTokenAsync(request.Token);
            if (savedToken == null || savedToken.IsUsed || savedToken.ExpiresAt < DateTime.UtcNow)
                return ApiResponse<bool>.ErrorResponse(400, "Link đặt lại mật khẩu không hợp lệ hoặc đã hết hạn");
            var userEmail = jwt.Claims.First(c => c.Type == "email").Value;
            var user = await _userRepository.GetByEmailAsync(userEmail);

            if (user == null)
                return ApiResponse<bool>.ErrorResponse(404, "Người dùng không tồn tại");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            savedToken.IsUsed = true;
            await _userRepository.UpdateResetTokenAsync(savedToken);
            await _userRepository.UpdateAsync(user);

            return ApiResponse<bool>.SuccessResponse(true, "Đặt lại mật khẩu thành công");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }

    private string GenerateJwtToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured");
        var jwtIssuer = _configuration["Jwt:Issuer"];
        var jwtAudience = _configuration["Jwt:Audience"];
        var expirationMinutes = int.Parse(_configuration["Jwt:ExpirationInMinutes"] ?? "1440");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("userId", user.Id.ToString()),
            new Claim("name", user.UserName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("role", user.Role?.Name ?? "User"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateResetPasswordToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"];
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey!));

        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("email", user.Email),
                new Claim("type", "reset-password")
            },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ApiResponse<AuthResponse>> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            var existingToken = await _refreshTokenRepository.GetByTokenAsync(refreshToken);
            if (existingToken == null || existingToken.ExpiredAt < DateTime.UtcNow || existingToken.RevokeAt != null)
                return ApiResponse<AuthResponse>.ErrorResponse(400, "Refresh token không hợp lệ hoặc đã hết hạn");
            var user = await _userRepository.GetByIdAsync(existingToken.UserId);
            await _refreshTokenRepository.RevokeAsync(existingToken.Token.ToString());
            var newRefreshToken = CreateNewRefreshToken(existingToken.UserId);
            await _refreshTokenRepository.SaveAsync(newRefreshToken);
            var accessToken = GenerateJwtToken(user);
            var response = new AuthResponse
            {
                Id = user.Id,
                Name = user.UserName,
                Email = user.Email,
                Token = accessToken,
                RefreshToken = newRefreshToken.Token.ToString()
            };
            return ApiResponse<AuthResponse>.SuccessResponse(response, "Làm mới token thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Refresh Token");
            return ApiResponse<AuthResponse>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }

    }
    private RefreshToken CreateNewRefreshToken(int userId)
    {
        var expirationDays = int.Parse(_configuration["Jwt:RefreshTokenExpiration"] ?? "7");
        var token = new RefreshToken
        {
            UserId = userId,
            Token = Guid.NewGuid(),
            ExpiredAt = DateTime.UtcNow.AddDays(expirationDays),
            CreatedAt = DateTime.UtcNow,
            RevokeAt = null
        };
        return token;
    }
    public async Task<ApiResponse<bool>> LogoutAsync(string refreshToken)
    {
        try
        {
            var existToken = await _refreshTokenRepository.GetByTokenAsync(refreshToken);
            if (existToken != null && existToken.RevokeAt == null)
            {
                await _refreshTokenRepository.RevokeAsync(existToken.Token.ToString());

            }
            return ApiResponse<bool>.SuccessResponse(true, "Đăng xuất thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Logout");

            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server, vui lòng thử lại sau");
        }
    }
}