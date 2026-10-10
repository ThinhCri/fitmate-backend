using System.Text.Json.Serialization;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

public sealed record SessionPackageSessionView(
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("slot_start_utc")] DateTime SlotStartUtc,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("paid_allocation_vnd")] long PaidAllocationVnd);

public sealed record SessionPackageStopView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("approval_source")] string ApprovalSource,
    [property: JsonPropertyName("requested_at_utc")] DateTime RequestedAtUtc,
    [property: JsonPropertyName("pt_response_deadline_utc")] DateTime PtResponseDeadlineUtc,
    [property: JsonPropertyName("refund_quote_vnd")] long RefundQuoteVnd,
    [property: JsonPropertyName("finalized_at_utc")] DateTime? FinalizedAtUtc);

public sealed record SessionPackageView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("member_id")] Guid MemberId,
    [property: JsonPropertyName("pt_user_id")] Guid PtUserId,
    [property: JsonPropertyName("service_id")] Guid ServiceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("session_count")] int SessionCount,
    [property: JsonPropertyName("total_price_vnd")] long TotalPriceVnd,
    [property: JsonPropertyName("hold_expires_at_utc")] DateTime HoldExpiresAtUtc,
    [property: JsonPropertyName("initial_schedule_deadline_utc")] DateTime InitialScheduleDeadlineUtc,
    [property: JsonPropertyName("make_up_deadline_utc")] DateTime MakeUpDeadlineUtc,
    [property: JsonPropertyName("payment_id")] Guid? PaymentId,
    [property: JsonPropertyName("payment_status")] string? PaymentStatus,
    [property: JsonPropertyName("refunded_so_far_vnd")] long RefundedSoFarVnd,
    [property: JsonPropertyName("stop_request")] SessionPackageStopView? StopRequest,
    [property: JsonPropertyName("sessions")] IReadOnlyList<SessionPackageSessionView> Sessions);

public sealed record SessionPackagePageView(
    [property: JsonPropertyName("items")] IReadOnlyList<SessionPackageView> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);

public interface ISessionPackageReadStore
{
    Task<SessionPackagePageView> ListAsync(Guid actorId, bool asPt,
        int page, int pageSize, CancellationToken ct);
    Task<SessionPackageView?> FindForParticipantAsync(Guid packageId,
        Guid actorId, CancellationToken ct);
}

public sealed class SessionPackageReadService(
    ISessionPackageReadStore store, IAuthorizationGuard authorization)
{
    public Task<SessionPackagePageView> ListMineAsync(Guid actorId,
        int page, int pageSize, CancellationToken ct)
    {
        ValidateActor(actorId);
        ValidatePage(page, pageSize);
        return store.ListAsync(actorId, false, page, pageSize, ct);
    }

    public async Task<SessionPackagePageView> ListAssignedPtAsync(Guid actorId,
        int page, int pageSize, CancellationToken ct)
    {
        ValidateActor(actorId);
        ValidatePage(page, pageSize);
        await authorization.EnsureRoleAsync(actorId, "PT", ct);
        return await store.ListAsync(actorId, true, page, pageSize, ct);
    }

    public async Task<SessionPackageView> GetForParticipantAsync(Guid actorId,
        Guid packageId, CancellationToken ct)
    {
        ValidateActor(actorId);
        if (packageId == Guid.Empty)
            throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập.");
        return await store.FindForParticipantAsync(packageId, actorId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Không tìm thấy gói tập.");
    }

    private static void ValidateActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Trang phải từ 1 và kích thước trang từ 1 đến 100.",
                new Dictionary<string, string[]> { ["page"] = ["page >= 1"],
                    ["page_size"] = ["1..100"] });
    }
}
