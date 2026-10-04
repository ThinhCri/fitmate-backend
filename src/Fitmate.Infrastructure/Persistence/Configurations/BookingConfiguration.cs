using Fitmate.Domain.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> e)
    {
        e.ToTable("appointments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).IsRequired();
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.ServiceId).IsRequired();
        e.Property(x => x.SlotStartUtc).IsRequired();
        e.Property(x => x.SlotStartLocal)
            .IsRequired()
            .HasConversion(
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));
        e.Property(x => x.DurationMinutes).IsRequired();
        e.Property(x => x.Status).HasConversion<int>().HasColumnName("appointment_status");
        e.Property(x => x.LegacyStatus).HasColumnName("legacy_status");
        e.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
        e.Property(x => x.LegacyCompletionTimeUnknown).HasColumnName("legacy_completion_time_unknown");
        e.Property(x => x.HoldExpiresAtUtc).HasColumnName("hold_expires_at_utc");
        e.Property(x => x.AcceptanceDeadlineUtc).HasColumnName("acceptance_deadline_utc");
        e.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        e.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        e.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        e.Property(x => x.CompletionSubmittedAtUtc).HasColumnName("completion_submitted_at_utc");
        e.Property(x => x.MemberConfirmationDeadlineUtc).HasColumnName("member_confirmation_deadline_utc");
        e.Property(x => x.DisputedAtUtc).HasColumnName("disputed_at_utc");
        e.Property(x => x.RescheduledCount).HasColumnName("rescheduled_count");
        e.Property(x => x.PlatformFeeRate).HasColumnType("numeric(5,4)");
        e.HasIndex(x => new { x.PtUserId, x.SlotStartUtc });
        e.HasIndex(x => new { x.MemberId, x.SlotStartUtc });
        e.HasIndex(x => x.PaymentId);
    }
}

public class BookingHoldConfiguration : IEntityTypeConfiguration<BookingHold>
{
    public void Configure(EntityTypeBuilder<BookingHold> e)
    {
        e.ToTable("booking_holds");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).IsRequired();
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.ServiceId).IsRequired();
        e.Property(x => x.SlotStartUtc).IsRequired();
        e.Property(x => x.ExpiresAt).IsRequired();
        e.Property(x => x.Status).HasMaxLength(16).IsRequired();
        e.HasIndex(x => x.ExpiresAt);
    }
}

public class PtScheduleLockConfiguration : IEntityTypeConfiguration<PtScheduleLock>
{
    public void Configure(EntityTypeBuilder<PtScheduleLock> builder)
    {
        builder.ToTable("pt_schedule_locks");
        builder.HasKey(x => x.PtUserId);
        builder.Property(x => x.PtUserId).HasColumnName("pt_user_id");
        builder.Property(x => x.LockedAtUtc).HasColumnName("locked_at_utc").IsRequired();
    }
}

public class BookingRescheduleRequestConfiguration : IEntityTypeConfiguration<BookingRescheduleRequest>
{
    public void Configure(EntityTypeBuilder<BookingRescheduleRequest> b)
    {
        b.ToTable("booking_reschedule_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.AppointmentId).HasColumnName("appointment_id").IsRequired();
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        b.Property(x => x.OldSlotStartUtc).HasColumnName("old_slot_start_utc").IsRequired();
        b.Property(x => x.NewSlotStartUtc).HasColumnName("new_slot_start_utc").IsRequired();
        b.Property(x => x.DurationMinutes).HasColumnName("duration_minutes").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
        b.Property(x => x.DeadlineUtc).HasColumnName("deadline_utc").IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(x => x.RespondedAtUtc).HasColumnName("responded_at_utc");
        b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);

        b.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.AppointmentId)
            .HasDatabaseName("uq_booking_reschedule_pending")
            .IsUnique()
            .HasFilter("status = 0");
    }
}
