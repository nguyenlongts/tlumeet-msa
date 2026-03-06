namespace MeetingService.Domain.Models;

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<MeetingParticipant> Participants { get; set; } = new List<MeetingParticipant>();
}