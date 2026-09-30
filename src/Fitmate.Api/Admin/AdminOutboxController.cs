using Fitmate.Api.Auth;
using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

public record ReplayOutboxRequest(string Reason);

/// <summary>
/// BE-824: Quản lý Dead-Letter Queue của Outbox pattern cho Admin.
/// Cung cấp khả năng theo dõi tin nhắn lỗi và kích hoạt replay an toàn có kiểm toán (audit).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/outbox")]
public class AdminOutboxController : ControllerBase
{
    private readonly IAdminOutboxService _outboxService;
    private readonly ICurrentUserAccessor _current;

    public AdminOutboxController(IAdminOutboxService outboxService, ICurrentUserAccessor current)
    {
        _outboxService = outboxService;
        _current = current;
    }

    /// <summary>
    /// Thống kê tình trạng Outbox queue (pending, dead_letter, độ trễ bản ghi cũ nhất).
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await _outboxService.GetStatsAsync(_current.GetUserIdOrThrow(), ct);
        return Ok(new
        {
            pending_count = stats.PendingCount,
            dead_letter_count = stats.DeadLetterCount,
            oldest_pending_age_seconds = stats.OldestPendingAgeSeconds
        });
    }

    /// <summary>
    /// Danh sách các tin nhắn rơi vào dead-letter queue có phân trang và lọc theo type.
    /// </summary>
    [HttpGet("dead-letter")]
    public async Task<IActionResult> ListDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        var result = await _outboxService.ListDeadLettersAsync(_current.GetUserIdOrThrow(), page, pageSize, type, ct);
        return Ok(result);
    }

    /// <summary>
    /// Replay có kiểm toán một tin nhắn dead-letter với lý do nghiệp vụ.
    /// </summary>
    [HttpPost("dead-letter/{id:guid}/replay")]
    public async Task<IActionResult> ReplayDeadLetter(
        Guid id,
        [FromBody] ReplayOutboxRequest request,
        CancellationToken ct)
    {
        RequireKey();
        var adminId = _current.GetUserIdOrThrow();
        var result = await _outboxService.ReplayAsync(id, adminId, request?.Reason ?? string.Empty, ct);

        return Ok(new
        {
            id = result.Id,
            type = result.Type,
            status = result.Status,
            replayed_by = result.ReplayedBy,
            next_attempt_at = result.NextAttemptAt
        });
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", 400);
    }
}
