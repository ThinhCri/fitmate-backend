using System.Security.Claims;
using Fitmate.Api.Auth;
using Fitmate.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-220: Admin metrics endpoints (OpenAPI <c>/admin/action-required</c>).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin")]
public class AdminMetricsController : ControllerBase
{
    private readonly IAdminMetricsService _svc;
    private readonly IAdminOutboxService _outbox;
    private readonly ICurrentUserAccessor _current;
    private static readonly DateTime ProcessStartTimeUtc = DateTime.UtcNow;

    public AdminMetricsController(
        IAdminMetricsService svc,
        IAdminOutboxService outbox,
        ICurrentUserAccessor current)
    {
        _svc = svc;
        _outbox = outbox;
        _current = current;
    }

    [HttpGet("action-required")]
    public async Task<IActionResult> ActionRequired([FromQuery] int recentLimit = 10, CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var result = await _svc.GetActionRequiredAsync(adminId, recentLimit, ct);
        return Ok(result);
    }

    /// <summary>
    /// BE-824: Trả về tóm tắt metrics hệ thống có kiểm soát quyền Admin, không lộ thông tin cá nhân hay unauthenticated diagnostics.
    /// </summary>
    [HttpGet("metrics/summary")]
    public async Task<IActionResult> GetMetricsSummary(CancellationToken ct)
    {
        var outbox = await _outbox.GetStatsAsync(_current.GetUserIdOrThrow(), ct);

        var uptime = DateTime.UtcNow - ProcessStartTimeUtc;

        return Ok(new
        {
            service = "Fitmate.Api",
            uptime_seconds = (long)uptime.TotalSeconds,
            outbox = new
            {
                pending_count = outbox.PendingCount,
                dead_letter_count = outbox.DeadLetterCount,
                oldest_age_seconds = (long)outbox.OldestPendingAgeSeconds
            },
            status = outbox.DeadLetterCount > 0 ? "warning" : "healthy"
        });
    }

    [HttpGet("business-summary")]
    public async Task<IActionResult> GetBusinessSummary(
        [FromServices] AdminBusinessSummaryService service,
        [FromQuery] string range = "30d", CancellationToken ct = default)
        => Ok(await service.GetAsync(_current.GetUserIdOrThrow(), range, ct));

    [HttpGet("finance/transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromServices] AdminTransactionsService service,
        [FromQuery] string? type = null, [FromQuery] string? status = null,
        [FromQuery] Guid? referenceId = null,
        [FromQuery] DateTime? fromUtc = null, [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => Ok(await service.GetAsync(_current.GetUserIdOrThrow(), type, status, referenceId,
            fromUtc, toUtc, page, pageSize, ct));

    [HttpGet("finance/revenue-report")]
    public async Task<IActionResult> GetRevenueReport(
        [FromServices] AdminRevenueReportService service,
        [FromQuery] string range = "30d", CancellationToken ct = default)
        => Ok(await service.GetAsync(_current.GetUserIdOrThrow(), range, ct));

    [HttpGet("analytics/overview")]
    public async Task<IActionResult> GetAnalyticsOverview(
        [FromServices] AdminAnalyticsService service,
        [FromQuery] string range = "30d", CancellationToken ct = default)
        => Ok(await service.GetAsync(_current.GetUserIdOrThrow(), range, ct));

    /// <summary>
    /// BE-824: Prometheus exposition format endpoint cho internal scraper / collector, bảo vệ bởi quyền AdminOnly.
    /// </summary>
    [HttpGet("metrics/prometheus")]
    public async Task<IActionResult> GetPrometheusMetrics(CancellationToken ct)
    {
        var outbox = await _outbox.GetStatsAsync(_current.GetUserIdOrThrow(), ct);

        Fitmate.Api.Observability.FitmateMetrics.UpdateOperationalGauges(
            outboxQueueDepth: outbox.PendingCount,
            outboxOldestAgeSeconds: outbox.OldestPendingAgeSeconds,
            refundsPendingCount: 0,
            refundsOldestAgeSeconds: 0);

        var prometheusText = Fitmate.Api.Observability.FitmateMetrics.GeneratePrometheusText();
        return Content(prometheusText, "text/plain; version=0.0.4; charset=utf-8");
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
