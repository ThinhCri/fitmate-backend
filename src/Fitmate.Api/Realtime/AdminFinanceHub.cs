using System.Security.Claims;
using Fitmate.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fitmate.Api.Realtime;

[Authorize(Policy = "AdminOnly")]
public sealed class AdminFinanceHub(IAuthorizationGuard authorization) : Hub
{
    public const string Group = "admin-finance";
    public override async Task OnConnectedAsync()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!Guid.TryParse(value, out var id)) throw new HubException("Unauthorized");
        await authorization.EnsureRoleAsync(id, "Admin", Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
