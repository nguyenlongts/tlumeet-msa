namespace MeetingService.Infrastructure.Messaging;

public class MeetingCreatedEvent
{
    public int MeetingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string HostEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class MeetingStartedEvent
{
    public int MeetingId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string HostEmail { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
}

public class MeetingEndedEvent
{
    public int MeetingId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string HostEmail { get; set; } = string.Empty;

    public DateTimeOffset EndedAt { get; set; }
}

public class ParticipantJoinedEvent
{
    public int ParticipantId { get; set; }
    public int MeetingId { get; set; }
    public string RoomCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? UserEmail { get; set; }

    public string ParticipantType { get; set; } = string.Empty; 

    public DateTimeOffset JoinedAt { get; set; }
}

public class ParticipantLeftEvent
{
    public int ParticipantId { get; set; }
    public int MeetingId { get; set; }

    public string RoomCode { get; set; } = string.Empty;

    public string? UserEmail { get; set; }

    public string ParticipantType { get; set; } = string.Empty;

    public DateTimeOffset LeftAt { get; set; }
}