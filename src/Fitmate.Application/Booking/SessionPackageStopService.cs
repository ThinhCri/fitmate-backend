using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

public interface ISessionPackageStopStore
{
    Task<bool> IsOwnedByAsync(Guid packageId, Guid memberId, CancellationToken ct);
    Task<bool> IsAssignedPtAsync(Guid packageId, Guid ptUserId, CancellationToken ct);
    Task<SessionPackageStopRequest> RequestAsync(Guid packageId, Guid memberId,
        string reason, CancellationToken ct);
    Task<SessionPackageStopRequest> AcceptByPtAsync(Guid packageId, Guid ptUserId, CancellationToken ct);
    Task<SessionPackageStopRequest> WithdrawAsync(Guid packageId, Guid memberId,
        Guid requestId, CancellationToken ct);
    Task<bool> ApproveTimedOutAsync(Guid packageId, DateTime nowUtc, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListTimedOutPackageIdsAsync(DateTime nowUtc, int limit, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListApprovedPackageIdsAsync(int limit, CancellationToken ct);
    Task<bool> FinalizeApprovedAsync(Guid packageId, DateTime nowUtc, CancellationToken ct);
}

/// <summary>
/// Member/PT stop commands. Each mutation is owner-scoped in this layer and
/// finalized asynchronously; refund transfer remains an Admin operation.
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

    public async Task<SessionPackageStopRequest> AcceptByPtAsync(Guid ptUserId, Guid packageId,
        CancellationToken ct)
    {
        if (ptUserId == Guid.Empty || packageId == Guid.Empty ||
            !await store.IsAssignedPtAsync(packageId, ptUserId, ct))
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập được phân công.");
        return await store.AcceptByPtAsync(packageId, ptUserId, ct);
    }

    public async Task<SessionPackageStopRequest> WithdrawAsync(Guid memberId, Guid packageId,
        Guid requestId, CancellationToken ct)
    {
        if (memberId == Guid.Empty || packageId == Guid.Empty || requestId == Guid.Empty ||
            !await store.IsOwnedByAsync(packageId, memberId, ct))
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy yêu cầu dừng gói.");
        return await store.WithdrawAsync(packageId, memberId, requestId, ct);
    }

    // Internal timeout command, invoked by the booking expiration worker.
    public Task<bool> ApproveTimedOutAsync(Guid packageId, DateTime nowUtc, CancellationToken ct) =>
        packageId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc
            ? throw new ArgumentException("A package ID and UTC time are required.")
            : store.ApproveTimedOutAsync(packageId, nowUtc, ct);

    public async Task<int> SweepTimedOutAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Sweep time must be UTC.", nameof(nowUtc));
        var ids = await store.ListTimedOutPackageIdsAsync(nowUtc, 50, ct);
        var approved = 0;
        foreach (var id in ids)
            if (await store.ApproveTimedOutAsync(id, nowUtc, ct)) approved++;
        return approved;
    }

    public Task<bool> FinalizeApprovedAsync(Guid packageId, DateTime nowUtc, CancellationToken ct) =>
        packageId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc
            ? throw new ArgumentException("A package ID and UTC time are required.")
            : store.FinalizeApprovedAsync(packageId, nowUtc, ct);

    public async Task<int> SweepApprovedAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Sweep time must be UTC.", nameof(nowUtc));
        var ids = await store.ListApprovedPackageIdsAsync(50, ct);
        var finalized = 0;
        foreach (var id in ids)
            if (await store.FinalizeApprovedAsync(id, nowUtc, ct)) finalized++;
        return finalized;
    }
}
