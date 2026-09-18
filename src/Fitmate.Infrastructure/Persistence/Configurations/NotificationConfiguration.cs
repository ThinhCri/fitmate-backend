using Fitmate.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> e)
    {
        e.ToTable("notifications");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.Type).HasConversion<int>();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        e.Property(x => x.PayloadJson).HasColumnType("jsonb");
        e.HasIndex(x => new { x.UserId, x.Read, x.CreatedAt });
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> e)
    {
        e.ToTable("devices");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        e.Property(x => x.Platform).HasConversion<int>();
        e.HasIndex(x => new { x.UserId, x.TokenHash }).IsUnique();
    }
}
