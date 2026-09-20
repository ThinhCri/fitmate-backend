using Fitmate.Domain.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class ChatThreadConfiguration : IEntityTypeConfiguration<ChatThread>
{
    public void Configure(EntityTypeBuilder<ChatThread> e)
    {
        e.ToTable("chat_threads");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200);
        e.Property(x => x.LastMessageAt).IsRequired();
    }
}

/// <summary>
/// BE-191: friend conversation với canonical pair (UserLowId &lt; UserHighId).
/// CHECK constraint đồng bộ với Friendship (DEC-037).
/// </summary>
public class FriendConversationConfiguration : IEntityTypeConfiguration<FriendConversation>
{
    public void Configure(EntityTypeBuilder<FriendConversation> e)
    {
        e.ToTable("friend_conversations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserLowId).IsRequired();
        e.Property(x => x.UserHighId).IsRequired();
        e.Property(x => x.LastMessageAt).IsRequired();

        e.HasIndex(x => new { x.UserLowId, x.UserHighId }).IsUnique();
        e.HasIndex(x => x.UserHighId);

        // Canonical pair: low < high (Postgres so sánh UUID qua byte order, khớp .NET Guid.CompareTo)
        e.ToTable(t => t.HasCheckConstraint(
            "ck_friend_conversations_canonical",
            "\"UserLowId\" < \"UserHighId\""));
    }
}

/// <summary>
/// BE-191: per-(conversation, user) read marker. PK composite.
/// </summary>
public class ChatThreadReadConfiguration : IEntityTypeConfiguration<ChatThreadRead>
{
    public void Configure(EntityTypeBuilder<ChatThreadRead> e)
    {
        e.ToTable("chat_thread_reads");
        e.HasKey(x => new { x.ConversationId, x.UserId });
        e.Property(x => x.ConversationId).HasColumnName("conversation_id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.LastReadMessageId).HasColumnName("last_read_message_id");
        e.Property(x => x.LastReadAt).HasColumnName("last_read_at");

        e.HasIndex(x => new { x.ConversationId, x.LastReadMessageId });
    }
}

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> e)
    {
        e.ToTable("chat_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ThreadId);
        e.Property(x => x.ConversationId);
        e.Property(x => x.Sender).HasConversion<int>();
        e.Property(x => x.SenderUserId);
        e.Property(x => x.Text).HasMaxLength(8000).IsRequired();
        e.Property(x => x.Confidence).HasColumnType("numeric(3,2)");
        e.Property(x => x.ClientMessageId);
        e.HasIndex(x => new { x.ThreadId, x.CreatedAt });

        // BE-191: pagination theo (conversation, createdAt desc, id desc)
        e.HasIndex(x => new { x.ConversationId, x.CreatedAt, x.Id });

        // BE-191: idempotency — client_message_id unique per conversation
        e.HasIndex(x => new { x.ConversationId, x.ClientMessageId })
            .IsUnique()
            .HasFilter("\"ConversationId\" IS NOT NULL AND \"ClientMessageId\" IS NOT NULL");
    }
}
