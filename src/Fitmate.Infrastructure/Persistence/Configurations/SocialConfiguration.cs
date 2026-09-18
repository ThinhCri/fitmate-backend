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
        e.Property(x => x.FromUserId).IsRequired();
        e.Property(x => x.ToUserId).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.HasIndex(x => new { x.FromUserId, x.ToUserId }).IsUnique();
    }
}

public class FriendConfiguration : IEntityTypeConfiguration<Friend>
{
    public void Configure(EntityTypeBuilder<Friend> e)
    {
        e.ToTable("friends");
        e.HasKey(x => new { x.UserId, x.FriendUserId });
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.FriendUserId).IsRequired();
    }
}
