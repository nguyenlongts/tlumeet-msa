using MeetingService.Domain.Enums;

namespace MeetingService.Domain.Models;

public class Meeting
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string HostEmail { get; set; } = string.Empty;

    public DateTimeOffset? ScheduledDateTime { get; set; }

    public int Duration { get; set; }

    public bool RequireHostToStart { get; set; } = true;

    public DateTimeOffset? ActualStartTime { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MeetingStatus Status { get; set; } = MeetingStatus.Scheduled;


    public ICollection<MeetingParticipant> Participants { get; set; }
        = new List<MeetingParticipant>();
}