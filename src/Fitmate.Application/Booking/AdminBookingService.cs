using Fitmate.Application.Auth;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

public class AdminBookingService : IAdminBookingService
{
    private readonly IAppointmentStore _appointments;
    private readonly IAuthorizationGuard _authz;
    private readonly IBookingRescheduleStore? _reschedules;

    public AdminBookingService(
        IAppointmentStore appointments,
        IAuthorizationGuard authz,
        IBookingRescheduleStore? reschedules = null)
    {
        _appointments = appointments;
        _authz = authz;
        _reschedules = reschedules;
    }

    public async Task<(IReadOnlyList<AdminBookingSummaryResponse> Items, int Total)> ListBookingsAsync(
        Guid adminId, AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : (pageSize > 100 ? 100 : pageSize);

        var (items, total) = await _appointments.ListAdminPagedAsync(status, ptUserId, memberId, page, pageSize, ct);
        var summaries = items.Select(a => new AdminBookingSummaryResponse(
            AppointmentId: a.Id,
            MemberId: a.MemberId,
            MemberName: null,
            PtUserId: a.PtUserId,
            PtName: null,
            ServiceId: a.ServiceId,
            ServiceName: null,
            SlotStartUtc: a.SlotStartUtc,
            DurationMinutes: a.DurationMinutes,
            PriceListedVnd: a.PriceListedVnd,
            ActualPaidVnd: a.ActualPaidVnd,
            AppointmentStatus: a.Status.ToString(),
            PaymentStatus: a.PaymentId != null ? "Paid" : null,
            RefundStatus: null
        )).ToList();

        return (summaries, total);
    }

    public async Task<AdminBookingDetailResponse?> GetBookingDetailAsync(
        Guid adminId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);

        var appt = await _appointments.FindByIdAsync(appointmentId, ct);
        if (appt == null) return null;

        string? rescheduleReason = null;
        if (_reschedules != null)
        {
            var resch = await _reschedules.FindPendingByAppointmentIdAsync(appointmentId, ct);
            rescheduleReason = resch?.Reason;
        }

        return new AdminBookingDetailResponse(
            AppointmentId: appt.Id,
            MemberId: appt.MemberId,
            MemberName: null,
            PtUserId: appt.PtUserId,
            PtName: null,
            ServiceId: appt.ServiceId,
            ServiceName: null,
            SlotStartUtc: appt.SlotStartUtc,
            DurationMinutes: appt.DurationMinutes,
            PriceListedVnd: appt.PriceListedVnd,
            ActualPaidVnd: appt.ActualPaidVnd,
            AppointmentStatus: appt.Status.ToString(),
            PaymentStatus: appt.PaymentId != null ? "Paid" : null,
            RefundStatus: null,
            CancellationReason: null,
            RescheduleReason: rescheduleReason,
            PaymentId: appt.PaymentId,
            Refunds: null
        );
    }
}
