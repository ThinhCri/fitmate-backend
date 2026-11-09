using Fitmate.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public sealed class AiPlayOrderConfiguration : IEntityTypeConfiguration<AiPlayOrder>
{
    public void Configure(EntityTypeBuilder<AiPlayOrder> e)
    {
        e.ToTable("ai_play_orders");
        e.HasKey(x => x.OrderId);
        e.Property(x => x.OrderId).HasMaxLength(128);
        e.Property(x => x.State).HasMaxLength(64).IsRequired();
        e.HasIndex(x => x.PurchasedAtUtc);
        e.HasIndex(x => x.SubscriptionId);
        e.HasOne<AiSubscriptionRecord>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
