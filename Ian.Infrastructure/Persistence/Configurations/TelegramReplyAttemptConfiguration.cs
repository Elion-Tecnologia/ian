using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class TelegramReplyAttemptConfiguration : IEntityTypeConfiguration<TelegramReplyAttempt>
{
    public void Configure(EntityTypeBuilder<TelegramReplyAttempt> builder)
    {
        builder.ToTable("telegram_reply_attempts", t =>
        {
            t.HasCheckConstraint("ck_reply_attempt_number", "\"Number\" BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_reply_transport_attempts", "\"TransportAttempts\" BETWEEN 0 AND 3");
        });
        builder.HasKey(a => new { a.UpdateId, a.Number });
        builder.Property(a => a.Status).HasMaxLength(64);
        builder.HasOne<TelegramAgentTurn>().WithMany().HasForeignKey(a => a.UpdateId).OnDelete(DeleteBehavior.Restrict);
    }
}
