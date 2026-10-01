using System.Text.Json.Serialization;
using Fitmate.Domain.Booking;

namespace Fitmate.Application.Booking;

/// <summary>
/// Canonical wire DTO for booking reschedule responses across Mobile and API consumers.
/// Decoupled from Domain entity.
/// </summary>
public sealed record BookingRescheduleResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    [property: JsonPropertyName("requested_by_user_id")] Guid RequestedByUserId,
    [property: JsonPropertyName("old_slot_start_utc")] DateTime OldSlotStartUtc,
    [property: JsonPropertyName("new_slot_start_utc")] DateTime NewSlotStartUtc,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("deadline_utc")] DateTime DeadlineUtc,
    [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("responded_at_utc")] DateTime? RespondedAtUtc,
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason
)
{
    public static BookingRescheduleResponse FromDomain(BookingRescheduleRequest r) =>
        new(
            Id: r.Id,
            AppointmentId: r.AppointmentId,
            RequestedByUserId: r.RequestedByUserId,
            OldSlotStartUtc: r.OldSlotStartUtc,
            NewSlotStartUtc: r.NewSlotStartUtc,
            DurationMinutes: r.DurationMinutes,
            Status: r.Status.ToString(),
            Reason: r.Reason,
            DeadlineUtc: r.DeadlineUtc,
            CreatedAtUtc: r.CreatedAtUtc,
            RespondedAtUtc: r.RespondedAtUtc,
            RejectionReason: r.RejectionReason
        );
}
