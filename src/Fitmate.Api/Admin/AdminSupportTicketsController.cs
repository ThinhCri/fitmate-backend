using Fitmate.Api.Auth;
using Fitmate.Application.Auth;
using Fitmate.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-819 / BR-14 / BR-15: Admin support ticket operations.
/// Allows administrators to inspect, reply, and update ticket lifecycle states.
/// </summary>
[ApiController]
[Route("api/v1/admin/support/tickets")]
[Authorize(Roles = "Admin")]
public class AdminSupportTicketsController : ControllerBase
{
    private readonly ISupportService _support;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuthorizationGuard _authz;

    public AdminSupportTicketsController(
        ISupportService support,
        ICurrentUserAccessor currentUser,
        IAuthorizationGuard authz)
    {
        _support = support;
        _currentUser = currentUser;
        _authz = authz;
    }

    [HttpGet]
    public async Task<IActionResult> ListTickets(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var adminId = _currentUser.GetUserIdOrThrow();
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        var result = await _support.ListAdminTicketsAsync(status, category, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTicket(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var adminId = _currentUser.GetUserIdOrThrow();
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        var ticket = await _support.GetTicketAsync(id, adminId, isAdmin: true, ct);
        if (ticket is null)
            return NotFound();

        return Ok(ticket);
    }

    [HttpPost("{id:guid}/reply")]
    public async Task<IActionResult> ReplyTicket(
        [FromRoute] Guid id,
        [FromBody] ReplySupportTicketRequest request,
        CancellationToken ct)
    {
        var adminId = _currentUser.GetUserIdOrThrow();
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        var ticket = await _support.AdminReplyTicketAsync(id, adminId, request, ct);
        if (ticket is null)
            return NotFound();

        return Ok(ticket);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        [FromRoute] Guid id,
        [FromBody] AdminChangeStatusRequest request,
        CancellationToken ct)
    {
        var adminId = _currentUser.GetUserIdOrThrow();
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        var ticket = await _support.AdminChangeStatusAsync(id, request, ct);
        if (ticket is null)
            return NotFound();

        return Ok(ticket);
    }
}

