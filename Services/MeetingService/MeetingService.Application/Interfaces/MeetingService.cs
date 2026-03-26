using MeetingService.Application.DTOs;
using MeetingService.Application.Interfaces;
using MeetingService.Domain.Enums;
using MeetingService.Domain.Interfaces;
using MeetingService.Domain.Models;
using MeetingService.Infrastructure;
using MeetingService.Infrastructure.Messaging;
using System.Text.Json;
namespace MeetingService.Application.Services;

public class MeetingService : IMeetingService
{
    private readonly IMeetingRepository _repository;

    private readonly MeetingDbContext _context;

    public MeetingService(
        IMeetingRepository repository, MeetingDbContext context)
    {
        _repository = repository;
        _context = context;
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
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _repository.CreateAsync(meeting);
            var outboxMessage = CreateOutboxMessage(nameof(MeetingCreatedEvent), new MeetingCreatedEvent
            {
                MeetingId = meeting.Id,
                Title = meeting.Title,
                RoomCode = meeting.RoomCode,
                HostEmail = meeting.HostEmail,
                CreatedAt = meeting.CreatedAt
            });

            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return ApiResponse<MeetingResponse>.SuccessResponse(MapMeeting(meeting));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<MeetingResponse>.ErrorResponse(500, "Lỗi server");
        }
    }

    public async Task<ApiResponse<MeetingResponse>> GetMeetingByIdAsync(int id)
    {
        var meeting = await _repository.GetByIdAsync(id);
        if (meeting == null)
            return ApiResponse<MeetingResponse>.ErrorResponse(404, "Phòng không tồn tại");

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
            return ApiResponse<bool>.ErrorResponse(404, "Phòng không tồn tại");
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _repository.DeleteAsync(id);
            var outboxMessage = CreateOutboxMessage(nameof(MeetingDeletedEvent), new MeetingDeletedEvent
            {
                MeetingId = meeting.Id,
                RoomCode = meeting.RoomCode,
                HostEmail = meeting.HostEmail,
                DeletedAt = DateTime.UtcNow
            });
            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Xoá phòng thành công");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server");
        }
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
            return ApiResponse<bool>.ErrorResponse(403, "Bạn không phải chủ phòng");
        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<bool>.ErrorResponse(400, "Phòng họp đã kết thúc");
        if (meeting.Status == MeetingStatus.Live)
            return ApiResponse<bool>.SuccessResponse(true);
      
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            meeting.ActualStartTime = DateTime.UtcNow;
            meeting.Status = MeetingStatus.Live;
            await _repository.UpdateAsync(meeting);
            var outboxMessage = CreateOutboxMessage(nameof(MeetingStartedEvent), new MeetingStartedEvent
            {
                MeetingId = meeting.Id,
                RoomCode = meeting.RoomCode,
                HostEmail = meeting.HostEmail,
                StartedAt = meeting.ActualStartTime.Value
            });
            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server");
        }
    }

    public async Task<ApiResponse<bool>> EndMeetingAsync(string roomCode, string hostEmail)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);

        if (meeting == null || meeting.HostEmail != hostEmail)
            return ApiResponse<bool>.ErrorResponse(403, "Not allowed");

        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<bool>.SuccessResponse(true);


        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
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
            var outboxMessage = CreateOutboxMessage(nameof(MeetingEndedEvent), new MeetingEndedEvent
            {
                MeetingId = meeting.Id,
                RoomCode = meeting.RoomCode,
                HostEmail = meeting.HostEmail,
                EndedAt = DateTime.UtcNow
            });
            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server");
        }
    }

    public async Task<ApiResponse<MeetingStatusDto>> GetMeetingStatusAsync(string roomCode)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);

        if (meeting == null)
            return ApiResponse<MeetingStatusDto>.ErrorResponse(404, "Phoingf không tồn tại");

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
    public async Task<ApiResponse<MeetingParticipantResponse>> JoinMeetingAsync(string roomCode, string? userEmail, string? guestName)
    {
        var meeting = await _repository.GetByRoomCodeAsync(roomCode);
        if (meeting == null)
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(404, "Phòng không tồn tại");
        if (meeting.Status == MeetingStatus.Ended)
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(400, "Phògn đã kết thúc");
        if (userEmail == null && string.IsNullOrWhiteSpace(guestName))
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(400, "Tên hiển thị không được để trống");
        int roleId;
        int? guestId = null;
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
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
            var outboxMessage = CreateOutboxMessage(nameof(ParticipantJoinedEvent), new ParticipantJoinedEvent
            {
                ParticipantId = participant.Id,
                MeetingId = participant.MeetingId,
                RoomCode = participant.RoomCode,
                DisplayName = participant.DisplayName,
                UserEmail = participant.UserEmail,
                ParticipantType = participant.UserEmail != null ? "User" : "Guest",
                JoinedAt = participant.JoinedAt
            });
            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<MeetingParticipantResponse>.SuccessResponse(
                MapParticipant(participant)
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<MeetingParticipantResponse>.ErrorResponse(500, "Lỗi server");
        }
    }

    public async Task<ApiResponse<bool>> LeaveMeetingAsync(string joinToken)
    {
        var participant = await _repository.GetParticipantByTokenAsync(joinToken);
        if (participant == null)
            return ApiResponse<bool>.ErrorResponse(404, "Người tham gia không tồn tại");
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            participant.LeftAt = DateTime.UtcNow;
            await _repository.UpdateParticipantAsync(participant);
            var outboxMessage = CreateOutboxMessage(nameof(ParticipantLeftEvent), new ParticipantLeftEvent
            {
                ParticipantId = participant.Id,
                MeetingId = participant.MeetingId,
                RoomCode = participant.RoomCode,
                DisplayName = participant.DisplayName,
                UserEmail = participant.UserEmail,
                ParticipantType = participant.UserEmail != null ? "User" : "Guest",
                LeftAt = participant.LeftAt.Value
            });
            await _context.OutboxMessages.AddAsync(outboxMessage);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<bool>.ErrorResponse(500, "Lỗi server");
        }
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
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            meeting.Title = request.Title;
            meeting.Description = request.Description;
            meeting.ScheduledDateTime = request.ScheduledDateTime;
            meeting.Duration = request.Duration;

            await _repository.UpdateAsync(meeting);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return ApiResponse<MeetingResponse>.SuccessResponse(MapMeeting(meeting), "Cập nhật thành công");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ApiResponse<MeetingResponse>.ErrorResponse(500, "Lỗi server");
        }
    }
    private OutboxMessage CreateOutboxMessage<T>(string eventType, T payload)
    {
        return new OutboxMessage
        {
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        };
    }

}
