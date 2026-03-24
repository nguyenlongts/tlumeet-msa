using MeetingService.Application.DTOs;
using MeetingService.Application.Interfaces;
using MeetingService.Domain.Enums;
using MeetingService.Domain.Interfaces;
using MeetingService.Domain.Models;
using MeetingService.Infrastructure.Messaging;
namespace MeetingService.Application.Services;

public class MeetingService : IMeetingService
{
    private readonly IMeetingRepository _repository;
    private readonly IKafkaProducer _kafkaProducer;

    public MeetingService(
        IMeetingRepository repository, IKafkaProducer kafkaProducer)
    {
        _repository = repository;
        _kafkaProducer = kafkaProducer;
    }


    public async Task<ApiResponse<MeetingResponse>> CreateMeetingAsync(CreateMeetingRequest request)
    {
        var meeting = new Meeting
        {
            Title = request.Title,
            Description = request.Description,
            HostEmail = request.HostEmail,
            ScheduledDateTime = request.ScheduledDateTime,
            Duration = request.Duration,
            RoomCode = Guid.NewGuid().ToString("N")[..8],
            CreatedAt = DateTime.UtcNow,

            RequireHostToStart = request.RequireHostToStart,

            Status = request.RequireHostToStart
                ? MeetingStatus.WaitingForHost
                : MeetingStatus.Scheduled
        };

        await _repository.CreateAsync(meeting);

        await _kafkaProducer.PublishAsync(
            KafkaTopics.MeetingCreated,
            new MeetingCreatedEvent
            {
                MeetingId = meeting.Id,
                Title = meeting.Title,
                RoomCode = meeting.RoomCode,
                HostEmail = meeting.HostEmail,
                CreatedAt = meeting.CreatedAt
            });

        return ApiResponse<MeetingResponse>.SuccessResponse(MapMeeting(meeting));
    }

    public async Task<ApiResponse<MeetingResponse>> GetMeetingByIdAsync(int id)
    {
        var meeting = await _repository.GetByIdAsync(id);
        if (meeting == null)
            return ApiResponse<MeetingResponse>.ErrorResponse(404, "Meeting not found");

        return ApiResponse<MeetingResponse>.SuccessResponse(MapMeeting(meeting));
    }

    public async Task<ApiResponse<List<MeetingResponse>>> GetAllMeetingsAsync()
    {
        var meetings = await _repository.GetAllAsync();
        return ApiResponse<List<MeetingResponse>>.SuccessResponse(
            meetings.Select(MapMeeting).ToList()
        );
    }

    public async Task<ApiResponse<List<MeetingResponse>>> GetMeetingsByHostEmailAsync(string hostEmail)
    {
        var meetings = await _repository.GetByHostEmailAsync(hostEmail);
        return ApiResponse<List<MeetingResponse>>.SuccessResponse(
            meetings.Select(MapMeeting).ToList()
        );
    }

    public async Task<ApiResponse<bool>> DeleteMeetingAsync(int id)
    {
        var meeting = await _repository.GetByIdAsync(id);
        if (meeting == null)
            return ApiResponse<bool>.ErrorResponse(404, "Meeting not found");
        await _repository.DeleteAsync(id);
        await _kafkaProducer.PublishAsync(KafkaTopics.MeetingDeleted, new MeetingDeletedEvent
        {
            MeetingId = id,
            RoomCode = meeting.RoomCode,
            HostEmail = meeting.HostEmail,
            DeletedAt = DateTime.UtcNow
        });
        return ApiResponse<bool>.SuccessResponse(true, "Deleted successfully");
    }

    public async Task<ApiResponse<bool>> CheckRoomCodeExistsAsync(string roomCode)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);
        return ApiResponse<bool>.SuccessResponse(meeting != null);
    }

    public async Task<ApiResponse<bool>> StartMeetingAsync(string roomCode, string hostEmail)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);
        if (meeting == null || meeting.HostEmail != hostEmail)
            return ApiResponse<bool>.ErrorResponse(403, "Not allowed");
        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<bool>.ErrorResponse(400, "Meeting already ended");
        if (meeting.Status == MeetingStatus.Live)
            return ApiResponse<bool>.SuccessResponse(true);
        meeting.ActualStartTime = DateTime.UtcNow;
        meeting.Status = MeetingStatus.Live;

        await _repository.UpdateAsync(meeting);
        await _kafkaProducer.PublishAsync(KafkaTopics.MeetingStarted, new MeetingStartedEvent
        {
            MeetingId = meeting.Id,
            RoomCode = meeting.RoomCode,
            HostEmail = meeting.HostEmail,
            StartedAt = meeting.ActualStartTime.Value
        });
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> EndMeetingAsync(string roomCode, string hostEmail)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);

        if (meeting == null || meeting.HostEmail != hostEmail)
            return ApiResponse<bool>.ErrorResponse(403, "Not allowed");

        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<bool>.SuccessResponse(true);

        meeting.Status = MeetingStatus.Ended;
        var participants = await _repository.GetParticipantsByRoomCodeAsync(roomCode);

        foreach (var p in participants)
        {
            if (p.LeftAt == null)
            {
                p.LeftAt = DateTime.UtcNow;
                await _repository.UpdateParticipantAsync(p);
            }
        }

        await _repository.UpdateAsync(meeting);

        await _kafkaProducer.PublishAsync(KafkaTopics.MeetingEnded,
            new MeetingEndedEvent
            {
                MeetingId = meeting.Id,
                RoomCode = meeting.RoomCode,
                EndedAt = DateTime.UtcNow
            });

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<MeetingStatusDto>> GetMeetingStatusAsync(string roomCode)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);

        if (meeting == null)
            return ApiResponse<MeetingStatusDto>.ErrorResponse(404, "Meeting not found");

        var dto = new MeetingStatusDto
        {
            RoomCode = meeting.RoomCode,
            HostName = meeting.HostEmail,
            RequireHostToStart = meeting.RequireHostToStart,
            Status = meeting.Status.ToString(),
            IsStarted = meeting.Status == MeetingStatus.Live,
            IsEnded = meeting.Status == MeetingStatus.Ended
        };

        return ApiResponse<MeetingStatusDto>.SuccessResponse(dto);
    }
    public async Task<ApiResponse<MeetingParticipantResponse>> JoinMeetingAsync( string roomCode, string? userEmail,string? guestName)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);
        if (meeting == null)
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(404, "Meeting not found");
        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(400, "Meeting ended");
        if (userEmail == null && string.IsNullOrWhiteSpace(guestName))
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(400, "Guest name is required");
        int roleId;
        int? guestId = null;

        if (userEmail != null)
        {
            roleId = (int)ParticipantRole.User;
        }
        else
        {
            roleId = (int)ParticipantRole.Guest;
            var guest = new Guest { DisplayName = guestName! };
            await _repository.AddGuestAsync(guest);
            guestId = guest.Id;
        }

        var participant = new MeetingParticipant
        {
            MeetingId = meeting.Id,
            RoomCode = meeting.RoomCode,
            DisplayName = userEmail ?? guestName!,
            UserEmail = userEmail,
            RoleId = roleId,
            GuestId = guestId,
            JoinToken = Guid.NewGuid().ToString()
        };

        await _repository.AddParticipantAsync(participant);
        await _kafkaProducer.PublishAsync(KafkaTopics.ParticipantJoined, new ParticipantJoinedEvent
        {
            ParticipantId = participant.Id,
            MeetingId = meeting.Id,
            RoomCode = meeting.RoomCode,
            DisplayName = participant.DisplayName,
            UserEmail = userEmail,
            ParticipantType = userEmail != null ? "User" : "Guest",
            JoinedAt = participant.JoinedAt
        });
        return ApiResponse<MeetingParticipantResponse>.SuccessResponse(
            MapParticipant(participant)
        );
    }

    public async Task<ApiResponse<bool>> LeaveMeetingAsync(string joinToken)
    {
        var participant = await _repository.GetParticipantByTokenAsync(joinToken);
        if (participant == null)
            return ApiResponse<bool>.ErrorResponse(404, "Participant not found");

        participant.LeftAt = DateTime.UtcNow;
        await _repository.UpdateParticipantAsync(participant);
        var meeting = await _repository.GetByRoomCodeAsync(participant.RoomCode);
        if (meeting != null && meeting.HostEmail == participant.UserEmail)
        {
            meeting.Status = MeetingStatus.Ended;

            await _repository.UpdateAsync(meeting);

            await _kafkaProducer.PublishAsync(KafkaTopics.MeetingEnded,
                new MeetingEndedEvent
                {
                    MeetingId = meeting.Id,
                    RoomCode = meeting.RoomCode,
                    EndedAt = DateTime.UtcNow
                });
        }

        await _kafkaProducer.PublishAsync(KafkaTopics.ParticipantLeft,
            new ParticipantLeftEvent
            {
                ParticipantId = participant.Id,
                MeetingId = participant.MeetingId,
                RoomCode = participant.RoomCode,
                UserEmail = participant.GuestId == null ? participant.DisplayName : null,
                ParticipantType = participant.GuestId == null ? "User" : "Guest",
                LeftAt = participant.LeftAt.Value
            });
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<List<MeetingParticipantResponse>>> GetParticipantsAsync(string roomCode)
    {
        var participants = await _repository.GetParticipantsByRoomCodeAsync(roomCode);

        return ApiResponse<List<MeetingParticipantResponse>>.SuccessResponse(
            participants.Select(MapParticipant).ToList()
        );
    }

    public async Task<ApiResponse<MeetingParticipantResponse>> GetParticipantByTokenAsync(string joinToken)
    {
        var participant = await _repository.GetParticipantByTokenAsync(joinToken);
        if (participant == null)
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(404, "Participant not found");

        return ApiResponse<MeetingParticipantResponse>.SuccessResponse(
            MapParticipant(participant)
        );
    }


    private static MeetingResponse MapMeeting(Meeting m)
    {
        return new MeetingResponse
        {
            Id = m.Id,
            Title = m.Title,
            Description = m.Description,
            RoomCode = m.RoomCode,
            HostName = m.HostEmail,
            ScheduledDateTime = m.ScheduledDateTime,
            Duration = m.Duration,
            CreatedAt = m.CreatedAt,
            MeetingLink = $"/meet/{m.RoomCode}",
            HostJoinLink = $"/meet/{m.RoomCode}?host=true"
        };
    }

    private static MeetingParticipantResponse MapParticipant(MeetingParticipant p)
    {
        return new MeetingParticipantResponse
        {
            ParticipantId = p.Id,
            RoomCode = p.RoomCode,
            DisplayName = p.DisplayName,
            Role = p.Role?.Name,
            JoinToken = p.JoinToken,
            JoinedAt = p.JoinedAt,
            LeftAt = p.LeftAt
        };
    }

    public async Task<ApiResponse<MeetingResponse>> UpdateMeetingAsync(UpdateMeetingRequest request)
    {
        var meeting = await _repository.GetByRoomCodeAsync(request.RoomCode);
        if (meeting == null)
            return ApiResponse<MeetingResponse>.ErrorResponse(404, "Meeting not found");
        if (meeting.HostEmail != request.HostEmail)
        {
            return ApiResponse<MeetingResponse>.ErrorResponse(403, "Not allowed");

        }
        meeting.Title = request.Title;
        meeting.Description = request.Description;
        meeting.ScheduledDateTime = request.ScheduledDateTime;
        meeting.Duration = request.Duration;

        await _repository.UpdateAsync(meeting);
        return ApiResponse<MeetingResponse>.SuccessResponse(MapMeeting(meeting), "Updated");
    }

}
