namespace AuthService.Domain.Models;

public class UserInfo
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Gender { get; set; }

    public User User { get; set; } = null!;
}