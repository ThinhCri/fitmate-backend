using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using Fitmate.Application.Trainers;

namespace Fitmate.Application.Booking;

/// <summary>
/// Internal reservation primitive. Package checkout must not call this until
/// one-order payment, callback, refund and payout consumers are package-aware.
/// </summary>
public sealed class SessionPackageHoldService(
    IAppointmentStore appointments,
    IBookingHoldStore holds,
    ISessionPackageStore packages,
    IServiceBookingLookup services,
    IOutboxDispatcher outbox,
    IGoogleMeetConnectionService? meet = null)
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromMinutes(10);

    public Task<SessionPackage> CreateInPersonHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId,
        IReadOnlyList<DateTime> slotStartsUtc, CancellationToken ct) =>
        CreateHoldAsync(memberId, ptUserId, serviceId, slotStartsUtc,
            ServiceDeliveryMode.InPerson, ct);

    public Task<SessionPackage> CreateOnlineHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId,
        IReadOnlyList<DateTime> slotStartsUtc, CancellationToken ct) =>
        CreateHoldAsync(memberId, ptUserId, serviceId, slotStartsUtc,
            ServiceDeliveryMode.Online, ct);

    private async Task<SessionPackage> CreateHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId,
        IReadOnlyList<DateTime> slotStartsUtc, ServiceDeliveryMode mode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slotStartsUtc);
        if (slotStartsUtc.Count is not (4 or 8))
            throw InvalidSlots("Chỉ hỗ trợ gói 4 hoặc 8 buổi.");
        if (memberId == Guid.Empty || ptUserId == Guid.Empty || serviceId == Guid.Empty ||
            memberId == ptUserId)
            throw InvalidSlots("Hội viên, PT và dịch vụ không hợp lệ.");

        var nowUtc = DateTime.UtcNow;
        var ordered = slotStartsUtc.OrderBy(x => x).ToArray();
        if (ordered.Any(x => x.Kind != DateTimeKind.Utc ||
                             x < nowUtc.AddMinutes(30) || x > nowUtc.AddDays(28)) ||
            ordered.Distinct().Count() != ordered.Length)
            throw InvalidSlots("Các buổi phải khác nhau, bắt đầu sau ít nhất 30 phút và trong 4 tuần.");

        // The DB-backed PT schedule lock serializes competing single and package holds.
        // All 4/8 rows and the outbox event commit (or roll back) together.
        var package = await appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            ServiceSnapshot? snapshot = null;
            foreach (var start in ordered)
            {
                var candidate = await services.FindBookableAsync(serviceId, start, ct);
                if (candidate is null || candidate.PtUserId != ptUserId ||
                    candidate.DeliveryMode != mode || candidate.PriceVnd <= 0)
                    throw new NotFoundException(ErrorCodes.General.NotFound,
                        "Dịch vụ hoặc một khung giờ không còn khả dụng cho gói tập.");
                if (snapshot is not null &&
                    (snapshot.PriceVnd != candidate.PriceVnd ||
                     snapshot.DurationMinutes != candidate.DurationMinutes))
                    throw new ConflictException(ErrorCodes.Booking.SlotTaken,
                        "Giá hoặc thời lượng dịch vụ đã thay đổi. Vui lòng chọn lại lịch.");
                snapshot = candidate;
            }

            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i] < ordered[i - 1].AddMinutes(snapshot!.DurationMinutes))
                    throw InvalidSlots("Các buổi trong cùng gói không được chồng giờ.");

            foreach (var start in ordered)
                if (await appointments.HasOverlappingSlotAsync(ptUserId, start,
                        snapshot!.DurationMinutes, null, ct))
                    throw new ConflictException(ErrorCodes.Booking.SlotTaken,
                        $"Khung giờ {start:O} đã được đặt.");

            var pairs = new List<(BookingHold Hold, Appointment Appointment)>(ordered.Length);
            foreach (var start in ordered)
            {
                var hold = BookingHold.Create(memberId, ptUserId, serviceId, start, HoldTtl);
                var appointment = Appointment.CreateHold(memberId, ptUserId, serviceId,
                    start, start, snapshot!.DurationMinutes, snapshot.PriceVnd, HoldTtl);
                pairs.Add((hold, appointment));
                await holds.AddAsync(hold, ct);
                await appointments.AddAsync(appointment, ct);
            }

            var package = SessionPackage.CreateHold(memberId, ptUserId, serviceId,
                snapshot!.PriceVnd,
                pairs.Select(p => (p.Hold.Id, p.Appointment.Id, p.Appointment.SlotStartUtc)).ToArray(),
                DateTime.UtcNow, HoldTtl);
            if (mode == ServiceDeliveryMode.Online)
                package.BeginOnlinePreparation(DateTime.UtcNow);
            await packages.AddAsync(package, ct);
            if (mode == ServiceDeliveryMode.InPerson)
                EnqueueHeld(package);
            return package;
        }, ct);

        if (mode == ServiceDeliveryMode.InPerson) return package;

        try
        {
            if (meet is null)
                throw new DomainException("GOOGLE_MEET_NOT_CONFIGURED", "Google Meet chưa được cấu hình.", 503);
            // Google I/O must run outside the PT schedule/DB transaction.
            foreach (var session in package.Sessions.OrderBy(s => s.Sequence))
            {
                var space = await meet.CreateForAppointmentAsync(ptUserId, session.AppointmentId, ct);
                if (string.IsNullOrWhiteSpace(space.Name) ||
                    !Uri.TryCreate(space.MeetingUri, UriKind.Absolute, out var meetingUri) ||
                    meetingUri.Scheme != Uri.UriSchemeHttps || meetingUri.Host != "meet.google.com")
                    throw new DomainException("GOOGLE_MEET_RESPONSE_INVALID",
                        "Google Meet trả về liên kết không hợp lệ.", 503);
            }

            await appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
            {
                await packages.ReloadAsync(package, ct);
                var nowUtc = DateTime.UtcNow;
                if (package.Status != SessionPackageStatus.OnlinePreparing ||
                    package.HoldExpiresAtUtc <= nowUtc ||
                    !await packages.AllReservationsActiveAsync(package, nowUtc, ct))
                    throw new ConflictException("PACKAGE_HOLD_EXPIRED",
                        "Gói tập hết thời gian giữ chỗ trong lúc chuẩn bị Google Meet.");
                package.CompleteOnlinePreparation(nowUtc);
                EnqueueHeld(package);
                return true;
            }, ct);
            return package;
        }
        catch
        {
            await appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
            {
                await packages.ReloadAsync(package, CancellationToken.None);
                if (package.Status != SessionPackageStatus.OnlinePreparing) return false;
                package.AbortOnlinePreparation(DateTime.UtcNow);
                foreach (var session in package.Sessions)
                {
                    var appointment = await appointments.FindByIdAsync(session.AppointmentId,
                        CancellationToken.None);
                    var hold = await holds.FindByIdAsync(session.HoldId, CancellationToken.None);
                    appointment?.ExpireHold();
                    hold?.Release();
                }
                outbox.Enqueue("booking.package_expired", new
                {
                    packageId = package.Id, memberId, ptUserId,
                    appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray()
                });
                return true;
            }, CancellationToken.None);
            if (meet is not null)
                foreach (var session in package.Sessions)
                    await meet.RemoveForAppointmentAsync(ptUserId, session.AppointmentId,
                        CancellationToken.None);
            throw;
        }
    }

    private void EnqueueHeld(SessionPackage package) => outbox.Enqueue("booking.package_held", new
    {
        packageId = package.Id, package.MemberId, package.PtUserId,
        appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray()
    });

    private static ValidationException InvalidSlots(string detail) =>
        new(ErrorCodes.Validation.FieldInvalid, detail,
            new Dictionary<string, string[]> { ["slot_starts_utc"] = [detail] });
}
