using Fitmate.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;
public sealed class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> b)
    {
        b.ToTable("auth_sessions"); b.HasKey(x => x.Id);
        b.Property(x => x.SecurityStamp).HasMaxLength(256);
        b.Property(x => x.AuthenticationMethod).HasMaxLength(16);
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
    }
}
public sealed class AdminLoginChallengeConfiguration : IEntityTypeConfiguration<AdminLoginChallenge>
{
    public void Configure(EntityTypeBuilder<AdminLoginChallenge> b)
    {
        b.ToTable("admin_login_challenges"); b.HasKey(x => x.TokenHash);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.SecurityStamp).HasMaxLength(256);
        b.HasIndex(x => x.ExpiresAt);
    }
}
