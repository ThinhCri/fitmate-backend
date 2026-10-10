using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

public interface ISessionPackageStopStore
{
    Task<bool> IsOwnedByAsync(Guid packageId, Guid memberId, CancellationToken ct);
    Task<SessionPackageStopRequest> RequestAsync(Guid packageId, Guid memberId,
        string reason, CancellationToken ct);
}

/// <summary>
/// Internal command only. Do not expose it until PT response, expiry and refund
/// settlement can complete the same lifecycle.
/// </summary>
public sealed class SessionPackageStopService(ISessionPackageStopStore store)
{
    public async Task<SessionPackageStopRequest> RequestAsync(Guid memberId, Guid packageId,
        string reason, CancellationToken ct)
    {
        if (memberId == Guid.Empty || packageId == Guid.Empty ||
            !await store.IsOwnedByAsync(packageId, memberId, ct))
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 10 or > 1000)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Vui lòng nhập lý do dừng gói từ 10 đến 1000 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = ["10–1000 characters"] });
        return await store.RequestAsync(packageId, memberId, reason.Trim(), ct);
    }
}
