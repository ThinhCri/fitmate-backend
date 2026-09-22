using Fitmate.Api.Auth;
using Fitmate.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Support;

/// <summary>
/// BE-819 / BR-15: Support ticket endpoints for regular users (members and trainers).
/// Allows creating tickets, viewing own tickets with IDOR protection, and replying/closing tickets.
/// </summary>
[ApiController]
[Route("api/v1/support/tickets")]
[Authorize]
public class SupportTicketsController : ControllerBase
{
    private readonly ISupportService _support;
    private readonly ICurrentUserAccessor _currentUser;

    public SupportTicketsController(ISupportService support, ICurrentUserAccessor currentUser)
    {
        _support = support;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTicket(
        [FromBody] CreateSupportTicketRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        var ticket = await _support.CreateTicketAsync(userId, request, ct);
        return CreatedAtAction(nameof(GetTicket), new { id = ticket.Id }, ticket);
    }

    [HttpGet]
    public async Task<IActionResult> ListMyTickets(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        var result = await _support.ListMyTicketsAsync(userId, status, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTicket(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        // Actor is not admin; ticket owner check is enforced inside SupportService.
        // Returns null if ticket does not exist or belongs to another user -> 404 (IDOR guard).
        var ticket = await _support.GetTicketAsync(id, userId, isAdmin: false, ct);
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
        var userId = _currentUser.GetUserIdOrThrow();
        var ticket = await _support.UserReplyTicketAsync(id, userId, request, ct);
        if (ticket is null)
            return NotFound();

        return Ok(ticket);
    }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> CloseTicket(
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserIdOrThrow();
        var success = await _support.UserCloseTicketAsync(id, userId, ct);
        if (!success)
            return NotFound();

        return NoContent();
    }
}

