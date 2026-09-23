using System.Security.Claims;
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
    private readonly Fitmate.Application.Outbox.IOutboxStore _outboxStore;
    private static readonly DateTime ProcessStartTimeUtc = DateTime.UtcNow;

    public AdminMetricsController(
        IAdminMetricsService svc,
        Fitmate.Application.Outbox.IOutboxStore outboxStore)
    {
        _svc = svc;
        _outboxStore = outboxStore;
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
        var pendingOutbox = await _outboxStore.GetPendingCountAsync(ct);
        var deadLetterOutbox = await _outboxStore.GetDeadLetterCountAsync(ct);
        var oldestPendingAge = await _outboxStore.GetOldestPendingAgeAsync(DateTime.UtcNow, ct);

        var uptime = DateTime.UtcNow - ProcessStartTimeUtc;

        return Ok(new
        {
            service = "Fitmate.Api",
            uptime_seconds = (long)uptime.TotalSeconds,
            outbox = new
            {
                pending_count = pendingOutbox,
                dead_letter_count = deadLetterOutbox,
                oldest_age_seconds = (long)(oldestPendingAge?.TotalSeconds ?? 0)
            },
            status = deadLetterOutbox > 0 ? "warning" : "healthy"
        });
    }

    /// <summary>
    /// BE-824: Prometheus exposition format endpoint cho internal scraper / collector, bảo vệ bởi quyền AdminOnly.
    /// </summary>
    [HttpGet("metrics/prometheus")]
    public async Task<IActionResult> GetPrometheusMetrics(CancellationToken ct)
    {
        var pendingOutbox = await _outboxStore.GetPendingCountAsync(ct);
        var deadLetterOutbox = await _outboxStore.GetDeadLetterCountAsync(ct);
        var oldestPendingAge = await _outboxStore.GetOldestPendingAgeAsync(DateTime.UtcNow, ct);

        Fitmate.Api.Observability.FitmateMetrics.UpdateOperationalGauges(
            outboxQueueDepth: pendingOutbox,
            outboxOldestAgeSeconds: oldestPendingAge?.TotalSeconds ?? 0,
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
