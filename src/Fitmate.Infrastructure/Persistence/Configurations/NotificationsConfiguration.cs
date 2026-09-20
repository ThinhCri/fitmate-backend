using Fitmate.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for Notification, Device, PushDelivery, and PushProviderConfig.
/// </summary>
public class NotificationsConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");

        b.HasKey(n => n.Id);

        // Source tracking for idempotency.
        // Partial unique index: (user_id, source_event_id, source_type) WHERE source_event_id IS NOT NULL.
        // We use a filtered unique index via HasFilter + IsUnique after setting the condition.
        b.Property(n => n.Id).HasColumnName("id");
        b.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        b.Property(n => n.Type).HasColumnName("type").HasConversion<int>().IsRequired();
        b.Property(n => n.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        b.Property(n => n.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        b.Property(n => n.Summary).HasColumnName("summary").HasMaxLength(120);
        b.Property(n => n.PayloadJson).HasColumnName("payload_json");
        b.Property(n => n.Read).HasColumnName("read").IsRequired();
        b.Property(n => n.SourceEventId).HasColumnName("source_event_id");
        b.Property(n => n.SourceType).HasColumnName("source_type").HasMaxLength(64);
        b.Property(n => n.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(n => n.UpdatedAt).HasColumnName("updated_at").IsRequired();
        b.Property(n => n.DeletedAt).HasColumnName("deleted_at");

        // Indexes.
        b.HasIndex(n => new { n.UserId, n.CreatedAt, n.Id })
            .HasDatabaseName("ix_notifications_user_created")
            .IsDescending(false, true, true);

        b.HasIndex(n => new { n.UserId, n.Read, n.CreatedAt })
            .HasDatabaseName("ix_notifications_user_read_created")
            .IsDescending(false, false, true);

        // Partial unique: only when source_event_id IS NOT NULL.
        b.HasIndex(n => new { n.UserId, n.SourceEventId, n.SourceType })
            .HasDatabaseName("ux_notifications_dedup")
            .IsUnique()
            .HasFilter("source_event_id IS NOT NULL");
    }
}

public class DevicesConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("devices");

        b.HasKey(d => d.Id);

        b.Property(d => d.Id).HasColumnName("id");
        b.Property(d => d.UserId).HasColumnName("user_id").IsRequired();
        b.Property(d => d.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        b.Property(d => d.EncryptedToken).HasColumnName("encrypted_token").IsRequired();
        b.Property(d => d.KeyId).HasColumnName("key_id").HasMaxLength(64).IsRequired();
        b.Property(d => d.Platform).HasColumnName("platform").HasConversion<int>().IsRequired();
        b.Property(d => d.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();
        b.Property(d => d.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(d => d.RevokedAt).HasColumnName("revoked_at");
        b.Property(d => d.LastUsedAt).HasColumnName("last_used_at");
        b.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Dedup: same (userId, tokenHash) is idempotent.
        b.HasIndex(d => new { d.UserId, d.TokenHash })
            .HasDatabaseName("ux_devices_user_token")
            .IsUnique();

        // Active-device lookup for push fan-out.
        b.HasIndex(d => d.UserId)
            .HasDatabaseName("ix_devices_active")
            .HasFilter("is_active = true");
    }
}

public class PushDeliveriesConfiguration : IEntityTypeConfiguration<PushDelivery>
{
    public void Configure(EntityTypeBuilder<PushDelivery> b)
    {
        b.ToTable("push_deliveries");

        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.NotificationId).HasColumnName("notification_id").IsRequired();
        b.Property(p => p.DeviceId).HasColumnName("device_id").IsRequired();
        b.Property(p => p.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        b.Property(p => p.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(p => p.LastAttemptAt).HasColumnName("last_attempt_at");
        b.Property(p => p.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(p => p.SentAt).HasColumnName("sent_at");
        b.Property(p => p.ExpiresAt).HasColumnName("expires_at").IsRequired();
        b.Property(p => p.SweepClaimAt).HasColumnName("sweep_claim_at");
        b.Property(p => p.LastError).HasColumnName("last_error").HasMaxLength(200);
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Indexes for sweep.
        b.HasIndex(p => new { p.Status, p.ExpiresAt })
            .HasDatabaseName("ix_push_deliveries_sweep")
            .HasFilter("status IN (0, 2, 3)"); // Pending, Deferred, FailedRetryable

        // Per-device tracking.
        b.HasIndex(p => p.NotificationId)
            .HasDatabaseName("ix_push_deliveries_notification");

        b.HasIndex(p => p.DeviceId)
            .HasDatabaseName("ix_push_deliveries_device");
    }
}

public class PushProviderConfigsConfiguration : IEntityTypeConfiguration<PushProviderConfig>
{
    public void Configure(EntityTypeBuilder<PushProviderConfig> b)
    {
        b.ToTable("push_provider_configs");

        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.Enabled).HasColumnName("enabled").IsRequired();
        b.Property(p => p.Provider).HasColumnName("provider").HasConversion<int>().IsRequired();
        b.Property(p => p.ConfigJson).HasColumnName("config_json");
        b.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
