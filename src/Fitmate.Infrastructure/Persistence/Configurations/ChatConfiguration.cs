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

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> e)
    {
        e.ToTable("chat_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ThreadId).IsRequired();
        e.Property(x => x.Sender).HasConversion<int>();
        e.Property(x => x.SenderUserId);
        e.Property(x => x.Text).HasMaxLength(8000).IsRequired();
        e.Property(x => x.Confidence).HasColumnType("numeric(3,2)");
        e.HasIndex(x => new { x.ThreadId, x.CreatedAt });
    }
}
