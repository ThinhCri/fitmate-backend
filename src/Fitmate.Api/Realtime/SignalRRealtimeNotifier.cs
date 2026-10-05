using Fitmate.Application.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Fitmate.Api.Realtime;

public sealed class SignalRRealtimeNotifier(
    IHubContext<FitmateRealtimeHub> hub,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload, CancellationToken ct)
    {
        var recipients = userIds.Where(id => id != Guid.Empty).Distinct().Select(id => id.ToString()).ToList();
        if (recipients.Count == 0) return;
        try
        {
            await hub.Clients.Users(recipients).SendAsync(eventName, payload, ct);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Realtime event {EventName} could not be delivered", eventName);
        }
    }
}
