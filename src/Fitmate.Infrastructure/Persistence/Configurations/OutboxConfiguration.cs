using Fitmate.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> e)
    {
        e.ToTable("outbox_messages");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.Type).HasMaxLength(120).IsRequired();
        e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.ProcessedAt);
        e.Property(x => x.NextAttemptAt).IsRequired();
        e.Property(x => x.Attempts).IsRequired();
        e.Property(x => x.Status).HasMaxLength(16).IsRequired();
        e.Property(x => x.LastError).HasMaxLength(1000);
        e.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> e)
    {
        e.ToTable("idempotency_records");
        e.HasKey(x => x.Key);
        e.Property(x => x.Key).HasMaxLength(120);
        e.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
        e.Property(x => x.StatusCode).IsRequired();
        e.Property(x => x.ResponseBody).HasColumnType("jsonb");
        e.Property(x => x.CreatedAt).IsRequired();
        e.Property(x => x.ExpiresAt).IsRequired();
    }
}
