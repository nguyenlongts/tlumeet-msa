namespace MeetingService.Domain.Interfaces;

using MeetingService.Domain.Models;

public interface IMeetingRepository
{
    Task<Meeting?> GetByIdAsync(int id);
    Task<Meeting?> GetByRoomCodeAsync(string roomCode);
    Task<List<Meeting>> GetAllAsync();
    Task<List<Meeting>> GetByHostEmailAsync(string hostEmail);
    Task<Meeting> CreateAsync(Meeting meeting);
    Task UpdateAsync(Meeting meeting);
    Task<bool> DeleteAsync(int id);
    Task<Meeting?> StartMeetingAsync(string roomCode, string hostEmail);
    Task<Meeting?> EndMeetingAsync(string roomCode, string hostEmail);

    Task<List<MeetingParticipant>> GetParticipantsByRoomCodeAsync(string roomCode);
    Task<MeetingParticipant?> GetParticipantByIdAsync(int id);
    Task<MeetingParticipant?> GetParticipantByTokenAsync(string joinToken);
    Task AddParticipantAsync(MeetingParticipant participant);
    Task UpdateParticipantAsync(MeetingParticipant participant);

    Task AddGuestAsync(Guest guest);
}