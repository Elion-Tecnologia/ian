using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class TipoServicoCatalogoPostgresTests
{
    [PostgresFact]
    public async Task FirstMigrationCreatesExactCatalogWithStableIds()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();

        var catalog = await ReadCatalogAsync(db);

        Assert.Equal(
            [
                new(TiposServicoPredefinidos.VisitaTecnicaId, TiposServicoPredefinidos.VisitaTecnicaNome,
                    240, UnidadeDuracao.Minutos, ModalidadeServico.Externo),
                new(TiposServicoPredefinidos.MontagemId, TiposServicoPredefinidos.MontagemNome,
                    7, UnidadeDuracao.DiasCorridos, ModalidadeServico.Externo),
                new(TiposServicoPredefinidos.AjusteId, TiposServicoPredefinidos.AjusteNome,
                    7, UnidadeDuracao.DiasCorridos, ModalidadeServico.Externo),
                new(TiposServicoPredefinidos.OrcamentoId, TiposServicoPredefinidos.OrcamentoNome,
                    1, UnidadeDuracao.DiasCorridos, ModalidadeServico.Interno)
            ],
            catalog);
        Assert.Equal(4, catalog.Select(tipo => tipo.Id).Distinct().Count());
        Assert.Equal(4, catalog.Select(tipo => tipo.Nome).Distinct().Count());
    }

    [PostgresFact]
    public async Task ReapplyingMigrationsDoesNotDuplicateOrChangeCatalog()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var before = await ReadCatalogAsync(db);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(before, await ReadCatalogAsync(db));
        Assert.Equal(4, await db.TiposServico.CountAsync());
    }

    [PostgresFact]
    public async Task NextGeneratedIdentityDoesNotReuseReservedIds()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var technical = new TipoServico
        {
            Nome = "Synthetic identity",
            DuracaoPadrao = 1,
            UnidadeDuracaoPadrao = UnidadeDuracao.Minutos,
            ModalidadePadrao = ModalidadeServico.Interno
        };

        db.TiposServico.Add(technical);
        await db.SaveChangesAsync();

        Assert.Equal(5, technical.Id);
        Assert.Equal([1L, 2L, 3L, 4L], await db.TiposServico
            .Where(tipo => tipo.Id <= 4)
            .OrderBy(tipo => tipo.Id)
            .Select(tipo => tipo.Id)
            .ToListAsync());
    }

    [PostgresFact]
    public async Task PreviouslyAdvancedIdentitySequenceDoesNotMoveBackward()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(SingleCalendarMigrationTests.Consolidation);
        await SingleCalendarMigrationTests.Sql(
            "SELECT setval('public.\"tipos_servico_Id_seq\"', 100, true)");

        await db.Database.MigrateAsync();

        Assert.Equal(100, await SingleCalendarMigrationTests.Scalar(
            "SELECT last_value FROM public.\"tipos_servico_Id_seq\""));
        Assert.Equal(1, await SingleCalendarMigrationTests.Scalar(
            "SELECT CASE WHEN is_called THEN 1 ELSE 0 END FROM public.\"tipos_servico_Id_seq\""));

        var technical = new TipoServico
        {
            Nome = "Synthetic advanced identity",
            DuracaoPadrao = 1,
            UnidadeDuracaoPadrao = UnidadeDuracao.Minutos,
            ModalidadePadrao = ModalidadeServico.Interno
        };
        db.TiposServico.Add(technical);
        await db.SaveChangesAsync();

        Assert.Equal(101, technical.Id);
    }

    [PostgresFact]
    public async Task ReadOnlyCatalogQueryReturnsDetachedItemsInStableOrder()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var query = new PostgresTipoServicoCatalogo(db);

        var catalog = await query.ListarAsync();

        Assert.Equal([1L, 2L, 3L, 4L], catalog.Select(tipo => tipo.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task AppointmentOverridesDoNotChangeCatalogDefaults()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var client = new Cliente { Nome = "Synthetic", TelefoneNormalizado = "synthetic-24" };
        db.Clientes.Add(client);
        await db.SaveChangesAsync();
        db.Agendamentos.Add(new Agendamento
        {
            ClienteId = client.Id,
            TipoServicoId = TiposServicoPredefinidos.VisitaTecnicaId,
            Modalidade = ModalidadeServico.Interno,
            Duracao = 30,
            UnidadeDuracao = UnidadeDuracao.Minutos,
            InicioPrevistoUtc = new DateTimeOffset(2030, 1, 7, 11, 0, 0, TimeSpan.Zero),
            FimPrevistoUtc = new DateTimeOffset(2030, 1, 7, 11, 30, 0, TimeSpan.Zero)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = await db.TiposServico.SingleAsync(
            tipo => tipo.Id == TiposServicoPredefinidos.VisitaTecnicaId);
        var appointment = await db.Agendamentos.SingleAsync();

        Assert.Equal(240, service.DuracaoPadrao);
        Assert.Equal(UnidadeDuracao.Minutos, service.UnidadeDuracaoPadrao);
        Assert.Equal(ModalidadeServico.Externo, service.ModalidadePadrao);
        Assert.Equal(30, appointment.Duracao);
        Assert.Equal(ModalidadeServico.Interno, appointment.Modalidade);
    }

    private static async Task InitializeAsync()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
    }

    private static async Task<IReadOnlyList<CatalogItem>> ReadCatalogAsync(IanDbContext db) =>
        await db.TiposServico
            .AsNoTracking()
            .OrderBy(tipo => tipo.Id)
            .Select(tipo => new CatalogItem(
                tipo.Id,
                tipo.Nome,
                tipo.DuracaoPadrao,
                tipo.UnidadeDuracaoPadrao,
                tipo.ModalidadePadrao))
            .ToListAsync();

    private sealed record CatalogItem(
        long Id,
        string Nome,
        int Duracao,
        UnidadeDuracao Unidade,
        ModalidadeServico Modalidade);
}
