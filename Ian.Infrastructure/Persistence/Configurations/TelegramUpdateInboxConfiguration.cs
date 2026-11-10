using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class TelegramUpdateInboxConfiguration
    : IEntityTypeConfiguration<TelegramUpdateInbox>
{
    public void Configure(EntityTypeBuilder<TelegramUpdateInbox> builder)
    {
        builder.ToTable("telegram_updates_inbox", table =>
        {
            table.HasCheckConstraint(
                "ck_telegram_inbox_update_id",
                "\"UpdateId\" >= 0");

            table.HasCheckConstraint(
                "ck_telegram_inbox_estado",
                """
                "Estado" IN (
                    'Received',
                    'Processing',
                    'Completed',
                    'FailedRetryable',
                    'FailedPaused'
                )
                """);

            table.HasCheckConstraint(
                "ck_telegram_inbox_tentativas",
                "\"Tentativas\" >= 0");

            table.HasCheckConstraint(
                "ck_telegram_inbox_lease",
                """
                (
                    "Estado" = 'Processing'
                    AND "LockToken" IS NOT NULL
                    AND "LockAteUtc" IS NOT NULL
                )
                OR
                (
                    "Estado" <> 'Processing'
                    AND "LockToken" IS NULL
                    AND "LockAteUtc" IS NULL
                )
                """);

            table.HasCheckConstraint(
                "ck_telegram_inbox_retry",
                """
                ("Estado" = 'FailedRetryable' AND "ProximaTentativaEmUtc" IS NOT NULL)
                OR
                ("Estado" <> 'FailedRetryable' AND "ProximaTentativaEmUtc" IS NULL)
                """);
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

        builder.Property(update => update.Tentativas)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(update => update.UltimoErroCodigo)
            .HasMaxLength(48);

        builder.Property(update => update.Resultado)
            .HasMaxLength(32);

        builder.HasIndex(update => new
        {
            update.Estado,
            update.ProximaTentativaEmUtc,
            update.RecebidoEmUtc
        });

        builder.HasIndex(update => new
        {
            update.Estado,
            update.LockAteUtc
        });
    }
}