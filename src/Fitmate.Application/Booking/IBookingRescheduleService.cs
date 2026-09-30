using System;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Domain.Booking;

namespace Fitmate.Application.Booking;

public interface IBookingRescheduleService
{
    Task<BookingRescheduleRequest> CreateRequestAsync(
        Guid requesterUserId,
        Guid appointmentId,
        DateTime newSlotStartUtc,
        string reason,
        CancellationToken ct);

    Task<BookingRescheduleRequest> AcceptRequestAsync(
        Guid responderUserId,
        Guid requestId,
        CancellationToken ct);

    Task<BookingRescheduleRequest> RejectRequestAsync(
        Guid responderUserId,
        Guid requestId,
        string? rejectionReason,
        CancellationToken ct);

    Task<BookingRescheduleRequest> CancelRequestAsync(
        Guid requesterUserId,
        Guid requestId,
        CancellationToken ct);
}
