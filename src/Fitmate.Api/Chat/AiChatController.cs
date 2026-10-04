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
        Ok(await _service.SendAsync(UserId(), body.ThreadId, body.Text, null, null, ct));

    [HttpPost("messages/image")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> SendWithImage(
        [FromForm(Name = "thread_id")] Guid threadId,
        [FromForm(Name = "text")] string text,
        [FromForm(Name = "file")] IFormFile file,
        CancellationToken ct)
    {
        var allowed = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
        if (file.Length is <= 0 or > 10 * 1024 * 1024 || !allowed.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Ảnh đính kèm không hợp lệ.",
                new Dictionary<string, string[]> { ["file"] = new[] { "must be JPEG, PNG, or WebP and at most 10 MB" } });
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        return Ok(await _service.SendAsync(UserId(), threadId, text, buffer.ToArray(), file.ContentType, ct));
    }

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
