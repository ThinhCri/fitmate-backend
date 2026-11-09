using Fitmate.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public sealed class AiSubscriptionConfiguration : IEntityTypeConfiguration<AiSubscriptionRecord>
{
    public void Configure(EntityTypeBuilder<AiSubscriptionRecord> e)
    {
        e.ToTable("ai_subscriptions");
        e.HasKey(x => x.Id);
        e.Property(x => x.PurchaseTokenHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.ProtectedPurchaseToken).IsRequired();
        e.Property(x => x.ProductId).HasMaxLength(128).IsRequired();
        e.Property(x => x.BasePlanId).HasMaxLength(128).IsRequired();
        e.Property(x => x.State).HasMaxLength(64).IsRequired();
        e.Property(x => x.LatestOrderId).HasMaxLength(128);
        e.HasIndex(x => x.PurchaseTokenHash).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
    }
}
