using Fitmate.Domain.Identity;
using Fitmate.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> e)
    {
        e.ToTable("support_tickets");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(256).IsRequired();
        e.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
        e.Property(x => x.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        e.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
        e.Property(x => x.ClosedAt).HasColumnName("closed_at");
        e.Property(x => x.BookingResolution).HasColumnName("booking_resolution")
            .HasConversion<string>().HasMaxLength(32);
        e.Property(x => x.BookingResolvedByAdminId).HasColumnName("booking_resolved_by_admin_id");
        e.Property(x => x.BookingResolvedAtUtc).HasColumnName("booking_resolved_at_utc");
        e.Property(x => x.BookingResolutionReason).HasColumnName("booking_resolution_reason").HasMaxLength(1000);
        e.Property(x => x.BookingEvidenceReference).HasColumnName("booking_evidence_reference").HasMaxLength(200);

        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasIndex(x => x.BookingId);
        e.HasIndex(x => new { x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.Category, x.CreatedAt });

        e.HasMany(x => x.Messages)
            .WithOne()
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        e.Navigation(x => x.Messages).AutoInclude(false);
    }
}

public class SupportTicketMessageConfiguration : IEntityTypeConfiguration<SupportTicketMessage>
{
    public void Configure(EntityTypeBuilder<SupportTicketMessage> e)
    {
        e.ToTable("support_ticket_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.TicketId).HasColumnName("ticket_id").IsRequired();
        e.Property(x => x.SenderId).HasColumnName("sender_id").IsRequired();
        e.Property(x => x.IsAdmin).HasColumnName("is_admin").IsRequired();
        e.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000).IsRequired();
        e.Property(x => x.AttachmentUrls).HasColumnName("attachment_urls");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        e.HasIndex(x => new { x.TicketId, x.CreatedAt });
    }
}

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> e)
    {
        e.ToTable("user_preferences");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        e.Property(x => x.Language).HasColumnName("language").HasMaxLength(16).IsRequired();
        e.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
        e.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(64).IsRequired();
        e.Property(x => x.NotificationsEnabled).HasColumnName("notifications_enabled").IsRequired();
        e.Property(x => x.EmailNotifications).HasColumnName("email_notifications").IsRequired();
        e.Property(x => x.PushNotifications).HasColumnName("push_notifications").IsRequired();
        e.Property(x => x.HealthDataSharingConsent).HasColumnName("health_data_sharing_consent").IsRequired();
        e.Property(x => x.BiometricLoginEnabled).HasColumnName("biometric_login_enabled").IsRequired();
        e.Property(x => x.TargetCalories).HasColumnName("target_calories");
        e.Property(x => x.TargetProteinG).HasColumnName("target_protein_g");
        e.Property(x => x.TargetCarbsG).HasColumnName("target_carbs_g");
        e.Property(x => x.TargetFatG).HasColumnName("target_fat_g");
        e.Property(x => x.TargetWeightKg).HasColumnName("target_weight_kg").HasPrecision(5, 1);
        e.Property(x => x.MonthlyWorkoutTarget).HasColumnName("monthly_workout_target");
        e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        e.HasIndex(x => x.UserId).IsUnique();
    }
}

