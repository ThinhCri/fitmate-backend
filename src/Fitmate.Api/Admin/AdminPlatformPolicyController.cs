using System.Security.Claims;
using Fitmate.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-151: Admin platform policy endpoint (OpenAPI <c>/admin/platform-policy</c>).
/// Read-only theo DEC-029.
/// Role enforcement 2 lớp: <see cref="AuthorizeAttribute"/> (policy <c>AdminOnly</c>) +
/// <see cref="IAuthorizationGuard"/> trong service (BE-090).
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin/platform-policy")]
public class AdminPlatformPolicyController : ControllerBase
{
    private readonly IPlatformPolicyService _svc;

    public AdminPlatformPolicyController(IPlatformPolicyService svc)
    {
        _svc = svc;
    }

    /// <summary>
    /// GET /api/v1/admin/platform-policy — trả platform policy hiện hành.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var policy = await _svc.GetCurrentAsync(adminId, ct);
        return Ok(policy);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
