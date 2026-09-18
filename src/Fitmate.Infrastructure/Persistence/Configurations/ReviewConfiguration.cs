using Fitmate.Domain.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> e)
    {
        e.ToTable("reviews");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.FromUserId).IsRequired();
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.Rating).IsRequired();
        e.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        e.HasIndex(x => new { x.PtUserId, x.CreatedAt });
    }
}
