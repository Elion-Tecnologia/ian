using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ian.Infrastructure.Persistence.Configurations;

internal sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes", table =>
            table.HasCheckConstraint(
                "ck_clientes_telefone_preenchido",
                """length(btrim("TelefoneNormalizado")) > 0"""));

        builder.HasKey(cliente => cliente.Id);
        builder.Property(cliente => cliente.Nome).IsRequired();
        builder.Property(cliente => cliente.TelefoneNormalizado).IsRequired();
        builder.HasIndex(cliente => cliente.TelefoneNormalizado).IsUnique();
    }
}

internal sealed class TipoServicoConfiguration : IEntityTypeConfiguration<TipoServico>
{
    public void Configure(EntityTypeBuilder<TipoServico> builder)
    {
        builder.ToTable("tipos_servico", table =>
        {
            table.HasCheckConstraint(
                "ck_tipos_servico_duracao_positiva",
                """ "DuracaoPadrao" > 0 """);
            table.HasCheckConstraint(
                "ck_tipos_servico_modalidade",
                """ "ModalidadePadrao" IN ('Interno', 'Externo', 'Remoto') """);
            table.HasCheckConstraint(
                "ck_tipos_servico_unidade_duracao",
                """ "UnidadeDuracaoPadrao" IN ('Minutos', 'DiasCorridos') """);
        });

        builder.HasKey(tipo => tipo.Id);
        builder.Property(tipo => tipo.Nome).IsRequired();
        builder.HasIndex(tipo => tipo.Nome).IsUnique();
        builder.Property(tipo => tipo.ModalidadePadrao).HasConversion<string>().HasMaxLength(16);
        builder.Property(tipo => tipo.UnidadeDuracaoPadrao).HasConversion<string>().HasMaxLength(16);

        builder.HasData(
            new TipoServico
            {
                Id = TiposServicoPredefinidos.VisitaTecnicaId,
                Nome = TiposServicoPredefinidos.VisitaTecnicaNome,
                DuracaoPadrao = 240,
                UnidadeDuracaoPadrao = UnidadeDuracao.Minutos,
                ModalidadePadrao = ModalidadeServico.Externo
            },
            new TipoServico
            {
                Id = TiposServicoPredefinidos.MontagemId,
                Nome = TiposServicoPredefinidos.MontagemNome,
                DuracaoPadrao = 7,
                UnidadeDuracaoPadrao = UnidadeDuracao.DiasCorridos,
                ModalidadePadrao = ModalidadeServico.Externo
            },
            new TipoServico
            {
                Id = TiposServicoPredefinidos.AjusteId,
                Nome = TiposServicoPredefinidos.AjusteNome,
                DuracaoPadrao = 7,
                UnidadeDuracaoPadrao = UnidadeDuracao.DiasCorridos,
                ModalidadePadrao = ModalidadeServico.Externo
            },
            new TipoServico
            {
                Id = TiposServicoPredefinidos.OrcamentoId,
                Nome = TiposServicoPredefinidos.OrcamentoNome,
                DuracaoPadrao = 1,
                UnidadeDuracaoPadrao = UnidadeDuracao.DiasCorridos,
                ModalidadePadrao = ModalidadeServico.Interno
            });
    }
}
