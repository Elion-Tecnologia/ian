
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class TelegramUpdateInboxConfiguration
    : IEntityTypeConfiguration<TelegramUpdateInbox>
{
    public void Configure(
        EntityTypeBuilder<TelegramUpdateInbox> builder)
    {
        builder.ToTable("telegram_updates_inbox", table =>
        {
            table.HasCheckConstraint(
                "ck_telegram_inbox_update_id",
                "\"UpdateId\" >= 0");

            table.HasCheckConstraint(
                "ck_telegram_inbox_estado",
                "\"Estado\" IN ('Received')");
        });

        builder.HasKey(update => update.UpdateId);

        builder.Property(update => update.UpdateId)
            .ValueGeneratedNever();

        builder.Property(update => update.PayloadJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(update => update.Estado)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(24);

        builder.Property(update => update.RecebidoEmUtc)
            .IsRequired();

        builder.HasIndex(update => new
        {
            update.Estado,
            update.RecebidoEmUtc
        });
    }
}
