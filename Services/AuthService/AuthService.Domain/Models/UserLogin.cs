namespace AuthService.Domain.Models;

public class UserLogin
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime? LastLogin { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? AccountLockedUntil { get; set; }

    public User User { get; set; } = null!;
}