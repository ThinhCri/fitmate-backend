using System.Text.Json.Serialization;
using Fitmate.Domain.Booking;

namespace Fitmate.Application.Booking;

/// <summary>
/// Canonical wire DTO for Appointment responses across Mobile and API consumers.
/// Decoupled from Domain entity; excludes internal audit, notes, legacy flags, and unbacked fields.
/// </summary>
public sealed record AppointmentResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("member_id")] Guid MemberId,
    [property: JsonPropertyName("pt_user_id")] Guid PtUserId,
    [property: JsonPropertyName("service_id")] Guid ServiceId,
    [property: JsonPropertyName("slot_start_utc")] DateTime SlotStartUtc,
    [property: JsonPropertyName("slot_start_local")] DateTime SlotStartLocal,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("hold_expires_at_utc")] DateTime? HoldExpiresAtUtc,
    [property: JsonPropertyName("acceptance_deadline_utc")] DateTime? AcceptanceDeadlineUtc,
    [property: JsonPropertyName("confirmed_at_utc")] DateTime? ConfirmedAtUtc,
    [property: JsonPropertyName("started_at_utc")] DateTime? StartedAtUtc,
    [property: JsonPropertyName("completed_at_utc")] DateTime? CompletedAtUtc,
    [property: JsonPropertyName("cancelled_at_utc")] DateTime? CancelledAtUtc,
    [property: JsonPropertyName("rescheduled_count")] int RescheduledCount,
    [property: JsonPropertyName("price_listed_vnd")] long PriceListedVnd,
    [property: JsonPropertyName("discount_vnd")] long DiscountVnd,
    [property: JsonPropertyName("actual_paid_vnd")] long ActualPaidVnd,
    [property: JsonPropertyName("platform_fee_rate_bps")] int PlatformFeeRateBps,
    [property: JsonPropertyName("platform_fee_vnd")] long PlatformFeeVnd,
    [property: JsonPropertyName("trainer_share_vnd")] long TrainerShareVnd,
    [property: JsonPropertyName("policy_version")] string PolicyVersion,
    [property: JsonPropertyName("payment_id")] Guid? PaymentId
)
{
    public static AppointmentResponse FromDomain(Appointment a)
    {
        var rawBps = (int)Math.Round(a.PlatformFeeRate * 10000m, MidpointRounding.AwayFromZero);
        var bps = Math.Clamp(rawBps, 0, 10000);

        return new AppointmentResponse(
            Id: a.Id,
            MemberId: a.MemberId,
            PtUserId: a.PtUserId,
            ServiceId: a.ServiceId,
            SlotStartUtc: a.SlotStartUtc,
            SlotStartLocal: a.SlotStartLocal,
            DurationMinutes: a.DurationMinutes,
            Status: a.Status.ToString(),
            HoldExpiresAtUtc: a.HoldExpiresAtUtc,
            AcceptanceDeadlineUtc: a.AcceptanceDeadlineUtc,
            ConfirmedAtUtc: a.ConfirmedAtUtc,
            StartedAtUtc: a.StartedAtUtc,
            CompletedAtUtc: a.CompletedAtUtc,
            CancelledAtUtc: a.CancelledAtUtc,
            RescheduledCount: a.RescheduledCount,
            PriceListedVnd: a.PriceListedVnd,
            DiscountVnd: a.DiscountVnd,
            ActualPaidVnd: a.ActualPaidVnd,
            PlatformFeeRateBps: bps,
            PlatformFeeVnd: a.PlatformFeeVnd,
            TrainerShareVnd: a.TrainerShareVnd,
            PolicyVersion: a.PolicyVersion ?? "v1",
            PaymentId: a.PaymentId
        );
    }
}

public sealed record AppointmentPageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<AppointmentResponse> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize
);
