using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Finance;

/// <summary>
/// BE-151: Read-only view của platform policy hiện hành (DEC-029).
/// Không có mutation trong release này.
/// </summary>
public interface IPlatformPolicyService
{
    Task<PlatformPolicy> GetCurrentAsync(Guid adminUserId, CancellationToken ct);
}

/// <summary>
/// Wire-format response cho GET /api/v1/admin/platform-policy.
/// Field names theo OpenAPI <c>PlatformPolicyResponse</c>.
/// </summary>
public sealed record PlatformPolicy(
    int PlatformFeeRateBps,
    string PolicyVersion,
    DateTime EffectiveFrom,
    bool Editable,
    string DisplayHint);

/// <summary>
/// BE-151 implementation. Đọc <see cref="PlatformPolicyOptions"/> từ DI,
/// validate bps, áp dụng AuthorizationGuard để chống bypass controller filter.
/// </summary>
public class PlatformPolicyService : IPlatformPolicyService
{
    private readonly PlatformPolicyOptions _options;
    private readonly IAuthorizationGuard _authz;

    /// <summary>
    /// Release đầu: KHÔNG cho phép admin sửa từ endpoint (xem DEC-029).
    /// </summary>
    private const bool IsEditable = false;

    public PlatformPolicyService(
        IOptions<PlatformPolicyOptions> options,
        IAuthorizationGuard authz)
    {
        _options = options.Value;
        _authz = authz;
    }

    public async Task<PlatformPolicy> GetCurrentAsync(Guid adminUserId, CancellationToken ct)
    {
        // Enforce role tại Application layer (BE-090) — chống bypass controller filter
        // hoặc direct call từ internal service.
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        // Validate bps để chắc chắn config sai không leak giá trị vô nghĩa ra ngoài.
        Money.ValidateBps(_options.FeeBps);

        var displayHint = _options.FeeBps == 1000
            ? "10.00% — cố định, chưa hỗ trợ chỉnh từ admin trong release đầu"
            : $"{Money.FormatPercent(_options.FeeBps)} — override qua env PLATFORM_FEE_BPS";

        return new PlatformPolicy(
            PlatformFeeRateBps: _options.FeeBps,
            PolicyVersion: _options.PolicyVersion,
            EffectiveFrom: _options.EffectiveFromUtc,
            Editable: IsEditable,
            DisplayHint: displayHint);
    }
}
