namespace NotificationService.API.Events;
public class PasswordResetEvent
{
    public string Email { get; set; } = string.Empty;
    public string ResetToken { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
public class UserRegisteredEvent
{
    public int UserId { get; set; }
    public string Email { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public DateTime RegisteredAt { get; set; }
}

public class PasswordChangedEvent
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}