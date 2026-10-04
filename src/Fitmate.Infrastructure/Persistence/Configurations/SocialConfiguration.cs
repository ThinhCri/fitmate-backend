using Fitmate.Domain.Social;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> e)
    {
        e.ToTable("posts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.AuthorId).IsRequired();
        e.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        e.Property(x => x.ImageStorageKey).HasMaxLength(500);
        e.Property(x => x.ImageStorageKeys).HasColumnType("text[]");
        e.Property(x => x.Link).HasMaxLength(500);
        e.Property(x => x.Tags).HasColumnType("text[]");
        e.Property(x => x.Visibility).HasConversion<int>();
        e.HasIndex(x => new { x.AuthorId, x.CreatedAt });
        e.HasIndex(x => x.CreatedAt);
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> e)
    {
        e.ToTable("comments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PostId).IsRequired();
        e.Property(x => x.AuthorId).IsRequired();
        e.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        e.HasIndex(x => new { x.PostId, x.CreatedAt });
    }
}

public class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> e)
    {
        e.ToTable("reactions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PostId).IsRequired();
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.Type).HasConversion<int>();
        e.HasIndex(x => new { x.PostId, x.UserId, x.Type }).IsUnique();
    }
}

public class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> e)
    {
        e.ToTable("friend_requests");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.SenderId).HasColumnName("sender_id").IsRequired();
        e.Property(x => x.RecipientId).HasColumnName("recipient_id").IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        // BE-181: chỉ 1 Pending request cho mỗi cặp — partial unique index.
        e.HasIndex(x => new { x.SenderId, x.RecipientId })
            .IsUnique()
            .HasFilter("\"Status\" = 0");
        e.HasIndex(x => new { x.RecipientId, x.Status });
    }
}

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> e)
    {
        e.ToTable("friendships");
        e.HasKey(x => new { x.UserLowId, x.UserHighId });
        e.Property(x => x.UserLowId).HasColumnName("user_low_id").IsRequired();
        e.Property(x => x.UserHighId).HasColumnName("user_high_id").IsRequired();
        // DB CHECK enforce canonical order; < so sánh UUID theo value.
        e.ToTable(t => t.HasCheckConstraint("ck_friendships_canonical_order", "\"user_low_id\" < \"user_high_id\""));
        e.HasIndex(x => x.UserHighId);
    }
}

public class BlockedUserConfiguration : IEntityTypeConfiguration<BlockedUser>
{
    public void Configure(EntityTypeBuilder<BlockedUser> e)
    {
        e.ToTable("blocked_users");
        e.HasKey(x => new { x.BlockerId, x.BlockedId });
        e.Property(x => x.BlockerId).HasColumnName("blocker_id").IsRequired();
        e.Property(x => x.BlockedId).HasColumnName("blocked_id").IsRequired();
        e.HasIndex(x => x.BlockedId);
    }
}
