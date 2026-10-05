using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fitmate.Api.Realtime;

[Authorize]
public sealed class FitmateRealtimeHub : Hub
{
}
