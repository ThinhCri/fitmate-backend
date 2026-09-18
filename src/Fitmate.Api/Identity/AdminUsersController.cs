using Fitmate.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Identity;

/// <summary>
/// BE-110: Admin user management.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _users;

    public AdminUsersController(IAdminUserService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string sort = "createdAt:desc",
        CancellationToken ct = default)
    {
        var statusFilter = ParseStatus(status);
        var q = new AdminUserListQuery(role, statusFilter, search, page, pageSize, sort);
        var (items, total) = await _users.ListAsync(q, ct);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await _users.GetAsync(id, ct));

    [HttpPost("{id:guid}/lock")]
    public async Task<IActionResult> SetLock(
        Guid id,
        [FromBody] SetLockRequest body,
        CancellationToken ct)
    {
        // AdminUserId từ JWT claim.
        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminId, out var adminGuid))
            return Unauthorized();

        var summary = await _users.SetLockAsync(id, adminGuid, body.Locked, body.Reason, ct);
        return Ok(summary);
    }

    private static AdminUserStatusFilter? ParseStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "active" => AdminUserStatusFilter.Active,
        "locked" => AdminUserStatusFilter.Locked,
        "pending_deletion" => AdminUserStatusFilter.PendingDeletion,
        _ => null
    };
}

public sealed record SetLockRequest(bool Locked, string Reason);
