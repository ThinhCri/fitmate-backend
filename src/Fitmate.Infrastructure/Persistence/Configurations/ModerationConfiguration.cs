using Fitmate.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> e)
    {
        e.ToTable("reports");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ReporterId).IsRequired();
        e.Property(x => x.TargetType).HasConversion<int>();
        e.Property(x => x.TargetId).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(64).IsRequired();
        e.Property(x => x.Details).HasMaxLength(2000);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.AdminNote).HasMaxLength(1000);
        // BE-211: idempotency key + action subtype.
        e.Property(x => x.IdempotencyKey).HasMaxLength(128);
        e.Property(x => x.ResolveAction).HasMaxLength(32);
        e.HasIndex(x => new { x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.TargetType, x.TargetId });
        // BE-211: idempotency-key uniqueness khi có key (partial unique — chỉ enforce khi client gửi).
        // Không dùng expiry trong index vì PG không hỗ trợ filter có now() trong unique partial index được —
        // TTL 24h sẽ enforce ở Application layer.
        e.HasIndex(x => new { x.ReporterId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        // BE-211: chống spam báo cáo trùng (DEC-028) — 1 user chỉ có 1 Open report cho cùng target.
        e.HasIndex(x => new { x.ReporterId, x.TargetType, x.TargetId })
            .IsUnique()
            .HasFilter("\"Status\" = 0"); // 0 = Pending
    }
}

public class UserModerationActionConfiguration : IEntityTypeConfiguration<UserModerationAction>
{
    public void Configure(EntityTypeBuilder<UserModerationAction> e)
    {
        e.ToTable("user_moderation_actions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.Action).HasConversion<int>();
        e.Property(x => x.ReportId);
        e.Property(x => x.ExpiresAt);
        e.Property(x => x.ActorAdminId).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.MetadataJson).HasColumnType("jsonb");
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.UpdatedAt).IsRequired();
        e.HasIndex(x => new { x.UserId, x.CreatedAt });
        e.HasIndex(x => x.ReportId);
    }
}
