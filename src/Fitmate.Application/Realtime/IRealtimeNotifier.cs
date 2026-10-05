namespace Fitmate.Application.Realtime;

public interface IRealtimeNotifier
{
    Task NotifyUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken ct);
}
