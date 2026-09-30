using System.Security.Claims;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-150: Admin finance endpoints (OpenAPI <c>/admin/finance/overview</c>, <c>/ledger</c>).
/// Role enforcement 2 lớp: Authorize attribute + Application AuthorizationGuard.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin/finance")]
public class AdminFinanceController : ControllerBase
{
    private readonly IAdminFinanceService _svc;

    public AdminFinanceController(IAdminFinanceService svc) { _svc = svc; }

    [HttpGet("payments/{paymentId:guid}/provider-status")]
    public async Task<IActionResult> ProviderStatus(Guid paymentId,
        [FromServices] Fitmate.Application.Auth.IAuthorizationGuard authz,
        [FromServices] Fitmate.Application.Payments.IPaymentService payments, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(GetUserId(), "Admin", ct);
        return Ok(await payments.QueryTransactionAsync(paymentId, ct));
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] string range = "30d", CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var parsed = ParseRange(range);
        var result = await _svc.GetOverviewAsync(adminId, parsed, ct);
        return Ok(result);
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(
        [FromQuery] string? type = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var adminId = GetUserId();
        LedgerEntryType? parsedType = string.IsNullOrEmpty(type) ? null : ParseType(type);
        var result = await _svc.GetLedgerAsync(adminId, parsedType, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("refunds")]
    public async Task<IActionResult> ProcessRefund(
        [FromBody] CreateRefundRequest body,
        CancellationToken ct = default)
    {
        RequireKey();
        var adminId = GetUserId();
        var result = await _svc.ProcessRefundAsync(adminId, body, ct);
        return Ok(result);
    }

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var result = await _svc.GetReconciliationAsync(adminId, ct);
        return Ok(result);
    }

    [HttpGet("legacy-appointments/unverified")]
    public async Task<IActionResult> GetUnverifiedLegacyAppointments(CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var result = await _svc.GetUnverifiedLegacyAppointmentsAsync(adminId, ct);
        return Ok(result);
    }

    [HttpPost("legacy-appointments/{id:guid}/verify")]
    public async Task<IActionResult> VerifyLegacyAppointment(
        Guid id,
        [FromBody] VerifyLegacyAppointmentRequest body,
        CancellationToken ct = default)
    {
        RequireKey();
        var adminId = GetUserId();
        var result = await _svc.ReconcileLegacyAppointmentAsync(adminId, id, body, ct);
        return Ok(new
        {
            appointmentId = result.Id,
            status = result.Status.ToString(),
            completedAtUtc = result.CompletedAtUtc,
            legacyCompletionTimeUnknown = result.LegacyCompletionTimeUnknown
        });
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", 400);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static FinanceRange ParseRange(string s) => s.ToLowerInvariant() switch
    {
        "7d" => FinanceRange._7d,
        "30d" => FinanceRange._30d,
        "90d" => FinanceRange._90d,
        "all" => FinanceRange.All,
        _ => throw new Fitmate.Domain.Common.ValidationException(
            Fitmate.Domain.Common.ErrorCodes.Validation.FieldInvalid,
            $"Unknown range '{s}'.",
            new Dictionary<string, string[]> { ["range"] = new[] { "7d|30d|90d|all" } })
    };

    private static LedgerEntryType ParseType(string s) => s.ToLowerInvariant() switch
    {
        "platform_fee" => LedgerEntryType.PlatformFee,
        "trainer_share" => LedgerEntryType.TrainerShare,
        "refund" => LedgerEntryType.Refund,
        "payout" => LedgerEntryType.Payout,
        _ => throw new Fitmate.Domain.Common.ValidationException(
            Fitmate.Domain.Common.ErrorCodes.Validation.FieldInvalid,
            $"Unknown ledger type '{s}'.",
            new Dictionary<string, string[]> { ["type"] = new[] { "platform_fee|trainer_share|refund|payout" } })
    };
}
