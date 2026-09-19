using System.Security.Claims;
using Fitmate.Application.Moderation;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-210: Admin reports endpoints (OpenAPI /admin/reports).
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin")]
public class AdminReportsController : ControllerBase
{
    private readonly IModerationService _svc;
    public AdminReportsController(IModerationService svc) { _svc = svc; }

    [HttpGet("reports")]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var statusFilter = ParseStatus(status);
        var result = await _svc.ListReportsAsync(adminId, statusFilter, page, pageSize, ct);
        return Ok(new { items = result.Items, total = result.Total });
    }

    [HttpPost("reports/{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveReportBody body, CancellationToken ct)
    {
        var adminId = GetUserId();
        var action = body.Action.ToLowerInvariant() switch
        {
            "warn" => ReportResolveAction.Warn,
            "lock_target" => ReportResolveAction.LockTarget,
            "delete_content" => ReportResolveAction.DeleteContent,
            "no_action" => ReportResolveAction.NoAction,
            _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "action invalid.",
                new Dictionary<string, string[]> { ["action"] = new[] { "warn|lock_target|delete_content|no_action" } })
        };
        var report = await _svc.ResolveReportAsync(adminId, id, action, body.Note, ct);
        return Ok(report);
    }

    [HttpPost("reports/{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, [FromBody] DismissReportBody body, CancellationToken ct)
    {
        var adminId = GetUserId();
        var report = await _svc.DismissReportAsync(adminId, id, body.Note, ct);
        return Ok(report);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }

    private static ReportStatus? ParseStatus(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.ToLowerInvariant() switch
        {
            "pending" => ReportStatus.Pending,
            "resolved" => ReportStatus.Resolved,
            "dismissed" => ReportStatus.Dismissed,
            _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "status invalid.",
                new Dictionary<string, string[]> { ["status"] = new[] { "pending|resolved|dismissed" } })
        };
    }
}

public sealed record ResolveReportBody(string Action, string? Note);
public sealed record DismissReportBody(string? Note);
