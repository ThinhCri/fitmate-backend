using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Chat;

[ApiController]
[Authorize]
[Route("api/v1/chat/ai")]
public sealed class AiChatController : ControllerBase
{
    private readonly IAiChatService _service;
    public AiChatController(IAiChatService service) => _service = service;

    [HttpGet("threads")]
    public async Task<IActionResult> ListThreads(CancellationToken ct) =>
        Ok(await _service.ListThreadsAsync(UserId(), ct));

    [HttpPost("threads")]
    public async Task<IActionResult> CreateThread([FromBody] CreateAiThreadBody body, CancellationToken ct)
    {
        var created = await _service.CreateThreadAsync(UserId(), body.Title, ct);
        return Created($"/api/v1/chat/ai/threads/{created.Id}", created);
    }

    [HttpGet("threads/{id:guid}/messages")]
    public async Task<IActionResult> ListMessages(Guid id, [FromQuery] int limit = 30, CancellationToken ct = default) =>
        Ok(new { items = await _service.ListMessagesAsync(UserId(), id, limit, ct), nextCursor = (string?)null });

    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] SendAiMessageBody body, CancellationToken ct) =>
        Ok(await _service.SendAsync(UserId(), body.ThreadId, body.Text, ct));

    private Guid UserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }
}

public sealed record CreateAiThreadBody([property: JsonPropertyName("title")] string? Title);
public sealed record SendAiMessageBody(
    [property: JsonPropertyName("thread_id")] Guid ThreadId,
    [property: JsonPropertyName("text")] string Text);
