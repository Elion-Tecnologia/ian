using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class TelegramBusinessOperationConfiguration
    : IEntityTypeConfiguration<TelegramBusinessOperation>
{
    public void Configure(
        EntityTypeBuilder<TelegramBusinessOperation> builder)
    {
        builder.ToTable("telegram_business_operations");

        builder.HasKey(operation => new
        {
            operation.UpdateId,
            operation.OperationKey
        });

        builder.Property(operation => operation.OperationKey)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(operation => operation.ExecutadoEmUtc)
            .IsRequired();

        builder.HasOne<TelegramUpdateInbox>()
            .WithMany()
            .HasForeignKey(operation => operation.UpdateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agendamento>()
            .WithMany()
            .HasForeignKey(operation => operation.AgendamentoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
