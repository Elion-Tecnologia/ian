using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class AgendamentoConfiguration : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> builder)
    {
        builder.ToTable("agendamentos", table =>
        {
            table.HasCheckConstraint("ck_agendamentos_versao", "\"Versao\" >= 1");
            table.HasCheckConstraint(
                "ck_agendamentos_periodo",
                """ "InicioPrevistoUtc" < "FimPrevistoUtc" """);

            table.HasCheckConstraint(
                "ck_agendamentos_duracao",
                """ "Duracao" > 0 """);

            table.HasCheckConstraint(
                "ck_agendamentos_unidade",
                """ "UnidadeDuracao" IN ('Minutos', 'DiasCorridos') """);

            table.HasCheckConstraint(
                "ck_agendamentos_modalidade",
                """ "Modalidade" IN ('Interno', 'Externo', 'Remoto') """);

            table.HasCheckConstraint(
                "ck_agendamentos_estado",
                """ "Estado" IN ('Agendado', 'Concluido', 'Cancelado') """);

            table.HasCheckConstraint(
                "ck_agendamentos_endereco_externo",
                """
                "Modalidade" <> 'Externo'
                OR ("EnderecoSnapshot" IS NOT NULL AND length(btrim("EnderecoSnapshot")) > 0)
                """);
        });

        builder.HasKey(agendamento => agendamento.Id);
        builder.Property(agendamento => agendamento.Versao)
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Property(agendamento => agendamento.Modalidade)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(agendamento => agendamento.UnidadeDuracao)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(agendamento => agendamento.Estado)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(agendamento => agendamento.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TipoServico>()
            .WithMany()
            .HasForeignKey(agendamento => agendamento.TipoServicoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(agendamento => new { agendamento.Estado, agendamento.InicioPrevistoUtc });
    }
}

internal sealed class PeriodoOcupacaoConfiguration : IEntityTypeConfiguration<PeriodoOcupacao>
{
    public void Configure(EntityTypeBuilder<PeriodoOcupacao> builder)
    {
        builder.ToTable("periodos_ocupacao", table =>
            table.HasCheckConstraint(
                "ck_periodos_ocupacao_intervalo",
                """ "InicioUtc" < "FimUtc" """));

        builder.HasKey(periodo => periodo.Id);

        builder.HasOne<Agendamento>()
            .WithMany()
            .HasForeignKey(periodo => periodo.AgendamentoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(periodo => new { periodo.AgendamentoId, periodo.InicioUtc });
    }
}
