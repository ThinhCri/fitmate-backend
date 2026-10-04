using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration cho <see cref="ApplicationUser"/> (DEC-032).
/// Theo Phương án A của <c>identity-evaluation.md §4</c>.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> e)
    {
        e.ToTable("asp_net_users");
        e.HasKey(x => x.Id);
        e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        e.Property(x => x.AvatarUrl).HasMaxLength(500);
        e.Property(x => x.AvatarStorageKey).HasMaxLength(1024);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.Timezone).HasMaxLength(64).IsRequired();
        e.Property(x => x.LastActiveAt);
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.UpdatedAt).IsRequired();
        e.Property(x => x.DeletedAt);
        e.HasIndex(x => x.DeletedAt).HasFilter("\"DeletedAt\" IS NULL");

        // Email là định danh (xem domain-rules.md §1).
        e.HasIndex(x => x.Email).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        e.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("\"DeletedAt\" IS NULL");

        // Soft-delete global query filter.
        e.HasQueryFilter(u => u.DeletedAt == null);

        // Concurrency token từ IdentityUser base (SecurityStamp + ConcurrencyStamp).
    }
}

/// <summary>
/// Configuration cho <see cref="IdentityRole{TKey}"/>.
/// </summary>
public class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> e)
    {
        e.ToTable("asp_net_roles");
        e.HasKey(x => x.Id);
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> e)
    {
        e.ToTable("user_profiles");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.HasIndex(x => x.UserId).IsUnique();
        e.Property(x => x.Phone).HasMaxLength(32);
        e.Property(x => x.Bio).HasMaxLength(500);
        e.Property(x => x.Gender).HasConversion<int?>();
        e.Property(x => x.HeightCm).HasColumnType("numeric(5,2)");
        e.Property(x => x.WeightKg).HasColumnType("numeric(5,2)");
        e.Property(x => x.TrainingDays).HasColumnType("text[]");
        e.Property(x => x.MedicalConditions).HasColumnType("text[]");
        e.Property(x => x.Allergies).HasColumnType("text[]");
        e.Property(x => x.PreferredTimeSlots).HasColumnType("text[]");
        e.Property(x => x.HealthNotes).HasMaxLength(2000);
        e.Property(x => x.MedicalSurveyDone);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("refresh_tokens");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => new { x.UserId, x.RevokedAt });
        e.Property(x => x.DeviceLabel).HasMaxLength(200);
        e.Property(x => x.ExpiresAt).IsRequired();
        e.Property(x => x.RevokedAt).IsConcurrencyToken();
        e.Property(x => x.SessionId);
        e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("audit_logs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ActorId);
        e.Property(x => x.ActorRole).HasMaxLength(32).IsRequired();
        e.Property(x => x.Action).HasMaxLength(64).IsRequired();
        e.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
        e.Property(x => x.TargetId);
        e.Property(x => x.Reason).HasMaxLength(500);
        e.Property(x => x.MetadataJson).HasColumnType("jsonb");
        e.Property(x => x.CreatedAt).IsRequired();
        e.HasIndex(x => x.CreatedAt);
    }
}
