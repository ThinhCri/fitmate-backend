using System.Security.Claims;

namespace Fitmate.Api.Auth;

/// <summary>
/// Helper đọc <c>userId</c> từ <see cref="ClaimsPrincipal"/> của request hiện tại.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Trả Guid user hiện tại, hoặc null nếu chưa authenticated.</summary>
    Guid? GetUserId();
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
}
