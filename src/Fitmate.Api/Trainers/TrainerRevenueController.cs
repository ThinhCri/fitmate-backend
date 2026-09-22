using System.Security.Claims;
using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

[ApiController]
[Route("api/v1")]
public class TrainerRevenueController : ControllerBase
{
    private readonly ITrainerRevenueService _svc;

    public TrainerRevenueController(ITrainerRevenueService svc)
    {
        _svc = svc;
    }

    /// <summary>PT xem tổng quan doanh thu của mình.</summary>
    [HttpGet("me/revenue")]
    [Authorize(Roles = "PT")]
    public async Task<IActionResult> GetMyRevenue(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] string range = "30d",
        CancellationToken ct = default)
    {
        var ptId = current.GetUserIdOrThrow();
        var parsedRange = ParseRange(range);
        var result = await _svc.GetOverviewAsync(ptId, parsedRange, ct);
        return Ok(result);
    }

    /// <summary>PT xem lịch sử doanh thu từng booking.</summary>
    [HttpGet("me/revenue/history")]
    [Authorize(Roles = "PT")]
    public async Task<IActionResult> GetMyRevenueHistory(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var ptId = current.GetUserIdOrThrow();
        var result = await _svc.GetHistoryAsync(ptId, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Admin thực hiện quyết toán / payout cho PT.</summary>
    [HttpPost("admin/finance/settlements")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SettleTrainer(
        [FromBody] TrainerSettlementRequest body,
        CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var result = await _svc.SettleTrainerAsync(adminId, body, ct);
        return Ok(result);
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
        _ => throw new ValidationException(
            ErrorCodes.Validation.FieldInvalid,
            $"Unknown range '{s}'.",
            new Dictionary<string, string[]> { ["range"] = new[] { "7d|30d|90d|all" } })
    };
}
