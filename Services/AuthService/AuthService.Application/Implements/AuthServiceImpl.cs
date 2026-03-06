using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using AuthService.Application.DTOs;
using AuthService.Application.Interfaces;
using AuthService.Domain.Interfaces;
using AuthService.Domain.Models;
using AuthService.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;

namespace AuthService.Application.Services.Implements;

public class AuthServiceImpl : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<AuthServiceImpl> _logger;
    public AuthServiceImpl(IUserRepository userRepository, IConfiguration configuration, IKafkaProducer kafkaProducer, ILogger<AuthServiceImpl> logger)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
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

            var createdUser = await _userRepository.CreateAsync(user);

            var token = GenerateJwtToken(createdUser);

            var response = new AuthResponse
            {
                Id = createdUser.Id,
                Name = createdUser.UserName,
                Email = createdUser.Email,
                Token = token
            };
            var userRegisteredEvent = new UserRegisteredEvent
            {
                UserId = createdUser.Id,
                Email = createdUser.Email,
                UserName = createdUser.UserName,
                RegisteredAt = DateTime.UtcNow
            };
            try
            {
                await _kafkaProducer.PublishAsync(KafkaTopics.UserRegistered, userRegisteredEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish UserRegistered event for UserId: {UserId}", createdUser.Id);
            }
            return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng ký thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in RegisterAsync");
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

            var isLocked = await _userRepository.IsAccountLockedAsync(user.Email);
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
                await _userRepository.IncrementFailedLoginAttemptsAsync(user.Email);

                var isLockedAfterFail = await _userRepository.IsAccountLockedAsync(user.Email);
                if (isLockedAfterFail)
                {
                    return ApiResponse<AuthResponse>.ErrorResponse(403,
                        "Tài khoản đã bị khóa do đăng nhập sai quá nhiều lần");
                }

                return ApiResponse<AuthResponse>.ErrorResponse(401, "Email hoặc mật khẩu không chính xác");
            }

            await _userRepository.ResetFailedLoginAttemptsAsync(user.Email);

            if (user.LoginInfo != null)
            {
                user.LoginInfo.LastLogin = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }

            var token = GenerateJwtToken(user);

            var response = new AuthResponse
            {
                Id = user.Id,
                Name = user.UserName,
                Email = user.Email,
                Token = token
            };

            return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng nhập thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in LoginAsync");
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

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);

            return ApiResponse<bool>.SuccessResponse(true, "Đổi mật khẩu thành công");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in LoginAsync");
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

            await _userRepository.ResetFailedLoginAttemptsAsync(user.Email);

            return ApiResponse<bool>.SuccessResponse(true, "Đã gửi email đặt lại mật khẩu");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(500, $"Lỗi server: {ex.Message}");
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
                jwt = handler.ReadJwtToken(request.Token);
            }
            catch
            {
                return ApiResponse<bool>.ErrorResponse(400, "Token không hợp lệ");
            }

            var type = jwt.Claims.FirstOrDefault(c => c.Type == "type")?.Value;
            if (type != "reset-password")
                return ApiResponse<bool>.ErrorResponse(400, "Token không hợp lệ");

            var userEmail = jwt.Claims.First(c => c.Type == "email").Value;
            var user = await _userRepository.GetByEmailAsync(userEmail);

            if (user == null)
                return ApiResponse<bool>.ErrorResponse(404, "Người dùng không tồn tại");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);

            return ApiResponse<bool>.SuccessResponse(true, "Đặt lại mật khẩu thành công");
        }
        catch (Exception ex)
        {
            return ApiResponse<bool>.ErrorResponse(500, $"Lỗi server: {ex.Message}");
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
            new Claim("role", user.Role?.Name),
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
}