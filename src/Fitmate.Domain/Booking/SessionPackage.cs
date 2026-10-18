using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Domain.Booking;

public enum SessionPackageStatus
{
    Held = 0,
    PaymentPending = 1,
    Active = 2,
    StopRequested = 3,
    Stopped = 4,
    Expired = 5,
    LatePaymentRefundPending = 6,
    OnlinePreparing = 7
}

public enum SessionPackageStopRequestStatus
{
    Pending = 0,
    Approved = 1,
    Withdrawn = 2
}

public enum SessionPackageStopApprovalSource
{
    None = 0,
    PtAccepted = 1,
    PtResponseTimedOut = 2
}

/// <summary>
/// Durable owner and financial snapshot for a four/eight-session purchase.
/// No public checkout route may use this until payment/refund/settlement all consume
/// the persisted per-session allocation.
/// </summary>
public sealed class SessionPackage : AuditableEntity
{
    private SessionPackage() { }

    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid PtUserId { get; private set; }
    public Guid ServiceId { get; private set; }
    public int SessionCount { get; private set; }
    public long TotalPriceVnd { get; private set; }
    public DateTime HoldExpiresAtUtc { get; private set; }
    public DateTime InitialScheduleDeadlineUtc { get; private set; }
    public DateTime MakeUpDeadlineUtc { get; private set; }
    public Guid? PaymentId { get; private set; }
    public SessionPackageStatus Status { get; private set; }
    public List<SessionPackageSession> Sessions { get; private set; } = [];
    public List<SessionPackageStopRequest> StopRequests { get; private set; } = [];

    public static SessionPackage CreateHold(Guid memberId, Guid ptUserId, Guid serviceId,
        long pricePerSessionVnd, IReadOnlyList<(Guid HoldId, Guid AppointmentId, DateTime SlotStartUtc)> slots,
        DateTime nowUtc, TimeSpan holdTtl, long? packageTotalPriceVnd = null)
    {
        if (memberId == Guid.Empty || ptUserId == Guid.Empty || serviceId == Guid.Empty || memberId == ptUserId)
            throw new ArgumentException("Package requires different valid member/PT identities and one service.");
        if (nowUtc.Kind != DateTimeKind.Utc || holdTtl <= TimeSpan.Zero)
            throw new ArgumentException("UTC time and a positive hold TTL are required.");
        if (slots.Count is not (4 or 8))
            throw new ArgumentException("Only four- and eight-session packages are supported.");
        if (pricePerSessionVnd <= 0)
            throw new ArgumentOutOfRangeException(nameof(pricePerSessionVnd));

        var ordered = slots.OrderBy(s => s.SlotStartUtc).ToArray();
        var initialDeadline = nowUtc.AddDays(28);
        for (var i = 0; i < ordered.Length; i++)
        {
            var slot = ordered[i];
            if (slot.HoldId == Guid.Empty || slot.AppointmentId == Guid.Empty || slot.SlotStartUtc.Kind != DateTimeKind.Utc ||
                slot.SlotStartUtc < nowUtc.AddMinutes(30) || slot.SlotStartUtc > initialDeadline ||
                (i > 0 && (ordered[i - 1].SlotStartUtc == slot.SlotStartUtc ||
                           ordered[i - 1].HoldId == slot.HoldId ||
                           ordered[i - 1].AppointmentId == slot.AppointmentId)))
                throw new ArgumentException("Package sessions need unique appointments and UTC slots within four weeks.");
        }
        if (ordered.Select(s => s.AppointmentId).Distinct().Count() != slots.Count)
            throw new ArgumentException("Appointment IDs must be unique.");
        if (ordered.Select(s => s.HoldId).Distinct().Count() != slots.Count)
            throw new ArgumentException("Hold IDs must be unique.");

        var total = packageTotalPriceVnd ?? checked(pricePerSessionVnd * slots.Count);
        if (total <= 0 || total > (decimal)pricePerSessionVnd * slots.Count)
            throw new ArgumentOutOfRangeException(nameof(packageTotalPriceVnd));
        var allocations = SessionPackageAllocation.Allocate(total, slots.Count);
        var package = new SessionPackage
        {
            Id = Guid.NewGuid(), MemberId = memberId, PtUserId = ptUserId, ServiceId = serviceId,
            SessionCount = slots.Count, TotalPriceVnd = total,
            HoldExpiresAtUtc = nowUtc.Add(holdTtl),
            InitialScheduleDeadlineUtc = initialDeadline,
            MakeUpDeadlineUtc = nowUtc.AddDays(42),
            Status = SessionPackageStatus.Held,
            CreatedAt = nowUtc, UpdatedAt = nowUtc
        };
        for (var i = 0; i < ordered.Length; i++)
            package.Sessions.Add(SessionPackageSession.Create(package.Id, ordered[i].HoldId, ordered[i].AppointmentId,
                ordered[i].SlotStartUtc, allocations[i], nowUtc));
        return package;
    }

    public void AttachPayment(Guid paymentId, DateTime nowUtc)
    {
        if (paymentId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc ||
            Status != SessionPackageStatus.Held || nowUtc >= HoldExpiresAtUtc)
            throw new InvalidOperationException("Only an active package hold can receive a payment.");
        PaymentId = paymentId;
        Status = SessionPackageStatus.PaymentPending;
        UpdatedAt = nowUtc;
    }

    public void BeginOnlinePreparation(DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.Held || PaymentId.HasValue ||
            nowUtc.Kind != DateTimeKind.Utc || nowUtc >= HoldExpiresAtUtc)
            throw new InvalidOperationException("Only an unpaid active package hold can prepare online sessions.");
        Status = SessionPackageStatus.OnlinePreparing;
        UpdatedAt = nowUtc;
    }

    public void CompleteOnlinePreparation(DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.OnlinePreparing || PaymentId.HasValue ||
            nowUtc.Kind != DateTimeKind.Utc || nowUtc >= HoldExpiresAtUtc)
            throw new InvalidOperationException("Online package preparation is no longer valid.");
        Status = SessionPackageStatus.Held;
        UpdatedAt = nowUtc;
    }

    public void AbortOnlinePreparation(DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.OnlinePreparing || PaymentId.HasValue ||
            nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only an unpaid online preparation can be aborted.");
        Status = SessionPackageStatus.Expired;
        UpdatedAt = nowUtc;
    }

    public void MarkPaid(Guid paymentId, long actualPaidVnd, DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.PaymentPending || PaymentId != paymentId ||
            actualPaidVnd != TotalPriceVnd || nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Package payment identity or amount does not match the quote.");
        Status = SessionPackageStatus.Active;
        UpdatedAt = nowUtc;
    }

    public void MarkLatePaymentForRefund(Guid paymentId, DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.PaymentPending || PaymentId != paymentId ||
            nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only the matching unpaid package can require a late-payment refund.");
        Status = SessionPackageStatus.LatePaymentRefundPending;
        UpdatedAt = nowUtc;
    }

    public void Expire(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc || nowUtc < HoldExpiresAtUtc ||
            Status is not (SessionPackageStatus.Held or SessionPackageStatus.OnlinePreparing) || PaymentId.HasValue)
            throw new InvalidOperationException("Only an unpaid expired package can be released.");
        Status = SessionPackageStatus.Expired;
        UpdatedAt = nowUtc;
    }

    public SessionPackageStopRequest RequestStop(string reason,
        IReadOnlyList<(Guid AppointmentId, AppointmentStatus Status, DateTime SlotStartUtc)> currentSessions,
        DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.Active ||
            StopRequests.Any(r => r.Status != SessionPackageStopRequestStatus.Withdrawn) ||
            nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only an active package without a stop request can be stopped.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10 || reason.Trim().Length > 1000)
            throw new ArgumentException("A stop reason of 10 to 1000 characters is required.", nameof(reason));
        if (currentSessions.Count != Sessions.Count ||
            currentSessions.Select(s => s.AppointmentId).Distinct().Count() != Sessions.Count ||
            currentSessions.Any(s => s.SlotStartUtc.Kind != DateTimeKind.Utc) ||
            currentSessions.Select(s => s.AppointmentId).ToHashSet()
                .SetEquals(Sessions.Select(s => s.AppointmentId)) is false)
            throw new ArgumentException("A current UTC snapshot of every package session is required.", nameof(currentSessions));

        var byAppointment = currentSessions.ToDictionary(s => s.AppointmentId);
        var snapshots = Sessions.OrderBy(s => s.Sequence).Select(s =>
        {
            var current = byAppointment[s.AppointmentId];
            return SessionPackageStopSession.Create(s.AppointmentId, current.Status,
                current.SlotStartUtc, s.PaidAllocationVnd,
                current.Status == AppointmentStatus.Confirmed &&
                current.SlotStartUtc >= nowUtc.AddHours(48));
        }).ToList();

        var stopRequest = SessionPackageStopRequest.Create(Id, reason.Trim(), nowUtc, snapshots);
        StopRequests.Add(stopRequest);
        Status = SessionPackageStatus.StopRequested;
        UpdatedAt = nowUtc;
        return stopRequest;
    }

    public void WithdrawStopRequest(Guid requestId, DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.StopRequested || nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only a requested package stop can be withdrawn.");
        var request = StopRequests.SingleOrDefault(r => r.Id == requestId)
            ?? throw new InvalidOperationException("Stop request does not belong to this package.");
        request.Withdraw(nowUtc);
        Status = SessionPackageStatus.Active;
        UpdatedAt = nowUtc;
    }

    public void FinalizeStopRequest(Guid requestId, DateTime nowUtc)
    {
        if (Status != SessionPackageStatus.StopRequested || nowUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Only a requested package stop can be finalized.");
        var request = StopRequests.SingleOrDefault(r => r.Id == requestId)
            ?? throw new InvalidOperationException("Stop request does not belong to this package.");
        request.FinalizeApproved(nowUtc);
        Status = SessionPackageStatus.Stopped;
        UpdatedAt = nowUtc;
    }
}

public sealed class SessionPackageStopRequest
{
    private SessionPackageStopRequest() { }

    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime PtResponseDeadlineUtc { get; private set; }
    public long RefundQuoteVnd { get; private set; }
    public SessionPackageStopRequestStatus Status { get; private set; }
    public SessionPackageStopApprovalSource ApprovalSource { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? WithdrawnAtUtc { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }
    public List<SessionPackageStopSession> Sessions { get; private set; } = [];

    internal static SessionPackageStopRequest Create(Guid packageId, string reason,
        DateTime nowUtc, List<SessionPackageStopSession> sessions)
    {
        var request = new SessionPackageStopRequest
        {
            Id = Guid.NewGuid(), PackageId = packageId, Reason = reason,
            RequestedAtUtc = nowUtc, PtResponseDeadlineUtc = nowUtc.AddHours(24),
            RefundQuoteVnd = sessions.Where(s => s.EligibleForRefund).Sum(s => s.PaidAllocationVnd),
            Status = SessionPackageStopRequestStatus.Pending,
            ApprovalSource = SessionPackageStopApprovalSource.None,
            Sessions = sessions
        };
        foreach (var session in sessions) session.Attach(request.Id);
        return request;
    }

    public bool ApproveByPt(DateTime nowUtc)
    {
        if (Status == SessionPackageStopRequestStatus.Approved &&
            ApprovalSource == SessionPackageStopApprovalSource.PtAccepted) return false;
        if (Status != SessionPackageStopRequestStatus.Pending || nowUtc.Kind != DateTimeKind.Utc ||
            nowUtc >= PtResponseDeadlineUtc)
            throw new InvalidOperationException("PT can accept only a pending request before its deadline.");
        Status = SessionPackageStopRequestStatus.Approved;
        ApprovalSource = SessionPackageStopApprovalSource.PtAccepted;
        ApprovedAtUtc = nowUtc;
        return true;
    }

    public bool ApproveAfterTimeout(DateTime nowUtc)
    {
        if (Status == SessionPackageStopRequestStatus.Approved &&
            ApprovalSource == SessionPackageStopApprovalSource.PtResponseTimedOut) return false;
        if (Status != SessionPackageStopRequestStatus.Pending || nowUtc.Kind != DateTimeKind.Utc ||
            nowUtc < PtResponseDeadlineUtc)
            throw new InvalidOperationException("Only a pending request past its deadline can time out.");
        Status = SessionPackageStopRequestStatus.Approved;
        ApprovalSource = SessionPackageStopApprovalSource.PtResponseTimedOut;
        ApprovedAtUtc = nowUtc;
        return true;
    }

    internal void Withdraw(DateTime nowUtc)
    {
        if (Status != SessionPackageStopRequestStatus.Pending || nowUtc.Kind != DateTimeKind.Utc ||
            nowUtc >= PtResponseDeadlineUtc)
            throw new InvalidOperationException("Only a pending stop request can be withdrawn before deadline.");
        Status = SessionPackageStopRequestStatus.Withdrawn;
        WithdrawnAtUtc = nowUtc;
    }

    internal void FinalizeApproved(DateTime nowUtc)
    {
        if (Status != SessionPackageStopRequestStatus.Approved || FinalizedAtUtc.HasValue ||
            nowUtc.Kind != DateTimeKind.Utc || !ApprovedAtUtc.HasValue || nowUtc < ApprovedAtUtc.Value)
            throw new InvalidOperationException("Only an approved, unfinalized stop request can be finalized.");
        FinalizedAtUtc = nowUtc;
    }
}

public sealed class SessionPackageStopSession
{
    private SessionPackageStopSession() { }

    public Guid Id { get; private set; }
    public Guid StopRequestId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public AppointmentStatus StatusAtRequest { get; private set; }
    public DateTime SlotStartAtRequestUtc { get; private set; }
    public long PaidAllocationVnd { get; private set; }
    public bool EligibleForRefund { get; private set; }

    internal static SessionPackageStopSession Create(Guid appointmentId, AppointmentStatus status,
        DateTime slotStartUtc, long paidAllocationVnd, bool eligible) => new()
        {
            Id = Guid.NewGuid(), AppointmentId = appointmentId, StatusAtRequest = status,
            SlotStartAtRequestUtc = slotStartUtc, PaidAllocationVnd = paidAllocationVnd,
            EligibleForRefund = eligible
        };

    internal void Attach(Guid stopRequestId) => StopRequestId = stopRequestId;
}

public sealed class SessionPackageSession
{
    private SessionPackageSession() { }

    public Guid Id { get; private set; }
    public Guid PackageId { get; private set; }
    public Guid HoldId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public int Sequence { get; private set; }
    public DateTime InitialSlotStartUtc { get; private set; }
    public long PaidAllocationVnd { get; private set; }
    public long PlatformFeeAllocationVnd { get; private set; }
    public long TrainerShareAllocationVnd { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static SessionPackageSession Create(Guid packageId, Guid holdId, Guid appointmentId,
        DateTime slotStartUtc, SessionPriceAllocation allocation, DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(), PackageId = packageId, HoldId = holdId, AppointmentId = appointmentId,
            Sequence = allocation.Sequence, InitialSlotStartUtc = slotStartUtc,
            PaidAllocationVnd = allocation.PaidVnd,
            PlatformFeeAllocationVnd = allocation.PlatformFeeVnd,
            TrainerShareAllocationVnd = allocation.TrainerShareVnd, CreatedAtUtc = nowUtc
        };
}
