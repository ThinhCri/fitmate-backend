using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

public record AdminBookingSummaryResponse(
    Guid AppointmentId,
    Guid MemberId,
    string? MemberName,
    Guid PtUserId,
    string? PtName,
    Guid ServiceId,
    string? ServiceName,
    DateTime SlotStartUtc,
    int DurationMinutes,
    long PriceListedVnd,
    long ActualPaidVnd,
    string AppointmentStatus,
    string? PaymentStatus,
    string? RefundStatus);

public record AdminBookingDetailResponse(
    Guid AppointmentId,
    Guid MemberId,
    string? MemberName,
    Guid PtUserId,
    string? PtName,
    Guid ServiceId,
    string? ServiceName,
    DateTime SlotStartUtc,
    int DurationMinutes,
    long PriceListedVnd,
    long ActualPaidVnd,
    string AppointmentStatus,
    string? PaymentStatus,
    string? RefundStatus,
    string? CancellationReason,
    string? RescheduleReason,
    Guid? PaymentId,
    IReadOnlyList<object>? Refunds);

public interface IAdminBookingService
{
    Task<(IReadOnlyList<AdminBookingSummaryResponse> Items, int Total)> ListBookingsAsync(
        Guid adminId, AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct);

    Task<AdminBookingDetailResponse?> GetBookingDetailAsync(
        Guid adminId, Guid appointmentId, CancellationToken ct);
}
