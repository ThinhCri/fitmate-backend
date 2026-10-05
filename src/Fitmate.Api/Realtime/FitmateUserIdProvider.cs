using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Fitmate.Api.Realtime;

public sealed class FitmateUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
           ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
