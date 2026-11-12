using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class TelegramAgentTurnConfiguration : IEntityTypeConfiguration<TelegramAgentTurn>
{
    public void Configure(EntityTypeBuilder<TelegramAgentTurn> builder)
    {
        builder.ToTable("telegram_agent_turns", table =>
        {
            table.HasCheckConstraint("ck_agent_turn_send_state", "\"SendState\" IN ('Pending','Sending','Accepted','Failed','Uncertain')");
            table.HasCheckConstraint("ck_agent_turn_send_attempts", "\"SendAttempts\" >= 0 AND \"SendAttempts\" <= 3");
        });
        builder.HasKey(turn => turn.UpdateId);
        builder.Property(turn => turn.UpdateId).ValueGeneratedNever();
        builder.Property(turn => turn.LogicalReplyKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(turn => turn.LogicalReplyKey).IsUnique();
        builder.HasIndex(turn => turn.NextSendAttemptAtUtc).HasFilter("\"NextSendAttemptAtUtc\" IS NOT NULL");
        builder.Property(turn => turn.CommandJson).HasColumnType("jsonb");
        builder.Property(turn => turn.ResponseText).HasMaxLength(4000);
        builder.Property(turn => turn.SendState).HasMaxLength(16);
        builder.Property(turn => turn.SendStatus).HasMaxLength(40);
        builder.HasOne<TelegramUpdateInbox>().WithOne().HasForeignKey<TelegramAgentTurn>(turn => turn.UpdateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
