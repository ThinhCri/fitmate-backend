namespace Fitmate.Application.Trainers;

public sealed record GoogleMeetConnectionStatus(bool Connected);
public sealed record GoogleMeetSpace(string Name, string MeetingUri);

public interface IGoogleMeetConnectionService
{
    Task<GoogleMeetConnectionStatus> GetStatusAsync(Guid ptUserId, CancellationToken ct);
    Task<string> CreateAuthorizationUrlAsync(Guid ptUserId, CancellationToken ct);
    Task CompleteAuthorizationAsync(string code, string state, CancellationToken ct);
    Task DisconnectAsync(Guid ptUserId, CancellationToken ct);
    Task<GoogleMeetSpace> CreateSpaceAsync(Guid ptUserId, CancellationToken ct);
    Task<GoogleMeetSpace> CreateForAppointmentAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);
    Task<string?> GetMeetingUriAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);
    Task<string?> GetMeetingUriForParticipantAsync(Guid actorUserId, Guid appointmentId, CancellationToken ct);
}
