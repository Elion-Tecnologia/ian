using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class SolicitacaoPendenteConfiguration
    : IEntityTypeConfiguration<SolicitacaoPendente>
{
    public void Configure(EntityTypeBuilder<SolicitacaoPendente> builder)
    {
        builder.ToTable("solicitacoes_pendentes", table =>
        {
            table.HasCheckConstraint(
                "ck_solicitacoes_expiracao",
                """ "CriadaEmUtc" < "ExpiraEmUtc" """);
            table.HasCheckConstraint(
                "ck_solicitacoes_estado",
                """ "Estado" IN ('Pendente', 'Confirmada', 'Cancelada', 'Expirada') """);
        });

        builder.HasKey(solicitacao => solicitacao.Id);
        builder.Property(solicitacao => solicitacao.Operacao)
            .IsRequired()
            .HasMaxLength(80);
        builder.Property(solicitacao => solicitacao.Estado)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasIndex(solicitacao => new
        {
            solicitacao.TelegramUserId,
            solicitacao.Estado,
            solicitacao.ExpiraEmUtc
        });
    }
}

internal sealed class RegistroAuditoriaConfiguration
    : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> builder)
    {
        builder.ToTable("registros_auditoria");
        builder.HasKey(registro => registro.Id);
        builder.Property(registro => registro.Operacao).IsRequired().HasMaxLength(80);
        builder.Property(registro => registro.Resultado).IsRequired().HasMaxLength(40);
        builder.Property(registro => registro.CorrelacaoId).IsRequired().HasMaxLength(128);

        builder.HasOne<Agendamento>()
            .WithMany()
            .HasForeignKey(registro => registro.AgendamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(registro => new { registro.AgendamentoId, registro.RegistradoEmUtc });
        builder.HasIndex(registro => registro.CorrelacaoId);
    }
}

internal sealed class EnvioTelegramConfiguration
    : IEntityTypeConfiguration<EnvioTelegram>
{
    public void Configure(EntityTypeBuilder<EnvioTelegram> builder)
    {
        builder.ToTable("envios_telegram", table =>
        {
            table.HasCheckConstraint(
                "ck_envios_telegram_tentativas",
                """ "Tentativas" >= 0 """);
            table.HasCheckConstraint(
                "ck_envios_telegram_estado",
                """ "Estado" IN ('Pendente', 'Enviado', 'Falhou') """);
        });

        builder.HasKey(envio => envio.Id);
        builder.Property(envio => envio.TipoMensagem).IsRequired().HasMaxLength(80);
        builder.Property(envio => envio.Estado).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(envio => new { envio.Estado, envio.CriadoEmUtc });
    }
}