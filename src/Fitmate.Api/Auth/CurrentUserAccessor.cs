using System.Security.Claims;
using Fitmate.Domain.Common;

namespace Fitmate.Api.Auth;

/// <summary>
/// Helper đọc <c>userId</c> từ <see cref="ClaimsPrincipal"/> của request hiện tại.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Trả Guid user hiện tại, hoặc null nếu chưa authenticated.</summary>
    Guid? GetUserId();

    /// <summary>
    /// Trả Guid user hiện tại; throw <see cref="UnauthorizedException"/> nếu chưa authenticated.
    /// </summary>
    Guid GetUserIdOrThrow();
}

public class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _http;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor http)
    {
        _http = http;
    }

    public Guid? GetUserId()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public Guid GetUserIdOrThrow() => GetUserId()
        ?? throw new UnauthorizedException(ErrorCodes.Auth.TokenInvalid, "User not authenticated.");
}
