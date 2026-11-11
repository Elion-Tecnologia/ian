using Ian.Application.Agenda;
using Ian.Domain.Entities;
using Ian.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
public sealed class ConsultaAgendaPostgresTests
{
    [PostgresFact]
    public async Task AgendaVaziaRetornaNenhumSemRastreamentoOuEscrita()
    {
        await InitializeAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var application = new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db));

        var resultado = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 7)));

        Assert.Equal(EstadoConsultaAgendamentos.Nenhum, resultado.Estado);
        Assert.Empty(resultado.Agendamentos);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, await db.Agendamentos.CountAsync());
        Assert.Equal(0, await db.PeriodosOcupacao.CountAsync());
    }

    [PostgresFact]
    public async Task ServicoDeSeteDiasApareceNaQuartaComPeriodoLocalESnapshotOriginal()
    {
        await InitializeAsync();
        var clienteId = await SeedClienteAsync("Maria", "11999999999", "Endereço atual");
        var agendamentoId = await SeedAgendamentoAsync(
            clienteId,
            TiposServicoPredefinidos.MontagemId,
            EstadoAgendamento.Agendado,
            ModalidadeServico.Externo,
            7,
            UnidadeDuracao.DiasCorridos,
            Enumerable.Range(0, 7)
                .Select(dia => Periodo(2030, 1, 7 + dia, 11, 21))
                .ToArray(),
            "Endereço snapshot");
        await using var update = PostgresTestDatabase.CreateContext();
        var cliente = await update.Clientes.SingleAsync(item => item.Id == clienteId);
        cliente.EnderecoPrincipal = "Endereço alterado";
        await update.SaveChangesAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        var application = new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db));

        var resultado = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 9)));
        var intervalo = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 8),
            DataFinal: new DateOnly(2030, 1, 10)));
        var disponibilidade = await new DisponibilidadeAgendaApplication(
            new PostgresConsultaAgendaStore(db)).VerificarAsync(new(
                new DateOnly(2030, 1, 9),
                new TimeOnly(8, 0),
                60,
                UnidadeDuracao.Minutos));

        Assert.Equal(EstadoConsultaAgendamentos.Unico, resultado.Estado);
        var item = Assert.Single(resultado.Agendamentos);
        Assert.Equal(agendamentoId, item.Id);
        Assert.Equal("Endereço snapshot", item.EnderecoServico);
        var periodo = Assert.Single(item.Periodos);
        Assert.Equal(new DateOnly(2030, 1, 9), periodo.Data);
        Assert.Equal(new TimeOnly(8, 0), periodo.Inicio);
        Assert.Equal(new TimeOnly(18, 0), periodo.Fim);
        var texto = FormatadorAgenda.Formatar(
            new DateOnly(2030, 1, 9),
            new DateOnly(2030, 1, 9),
            resultado.Agendamentos);
        Assert.Contains("08h–18h | Montagem — Maria", texto);
        Assert.Contains($"[#{agendamentoId}]", texto);
        Assert.Equal(3, Assert.Single(intervalo.Agendamentos).Periodos.Count);
        Assert.Contains(
            "Agenda de 08/01/2030 a 10/01/2030",
            FormatadorAgenda.Formatar(
                new DateOnly(2030, 1, 8),
                new DateOnly(2030, 1, 10),
                intervalo.Agendamentos));
        Assert.Equal(EstadoDisponibilidadeAgenda.Indisponivel, disponibilidade.Estado);
        Assert.Equal([agendamentoId], disponibilidade.AgendamentosConflitantes);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task FiltrosCombinadosIdEstadoEAmbiguidadeUsamOrdenacaoEstavelELimite()
    {
        await InitializeAsync();
        var maria = await SeedClienteAsync("Maria", "11999999999", null);
        var joao = await SeedClienteAsync("João", "21999999999", null);
        var primeiro = await SeedAgendamentoAsync(
            maria,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Agendado,
            ModalidadeServico.Interno,
            60,
            UnidadeDuracao.Minutos,
            [Periodo(2030, 1, 7, 11, 12)]);
        var segundo = await SeedAgendamentoAsync(
            maria,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Concluido,
            ModalidadeServico.Interno,
            60,
            UnidadeDuracao.Minutos,
            [Periodo(2030, 1, 7, 13, 14)]);
        await SeedAgendamentoAsync(
            joao,
            TiposServicoPredefinidos.OrcamentoId,
            EstadoAgendamento.Cancelado,
            ModalidadeServico.Interno,
            1,
            UnidadeDuracao.DiasCorridos,
            [Periodo(2030, 1, 7, 15, 21)]);
        await using var db = PostgresTestDatabase.CreateContext();
        var application = new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db));

        var combinado = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 7),
            ClienteId: maria,
            TipoServicoId: TiposServicoPredefinidos.VisitaTecnicaId,
            Estado: EstadoAgendamento.Agendado));
        var porId = await application.ConsultarAsync(new(AgendamentoId: segundo));
        var ambiguoLimitado = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 7),
            Limite: 2));

        Assert.Equal(primeiro, Assert.Single(combinado.Agendamentos).Id);
        Assert.Equal(EstadoAgendamento.Concluido, Assert.Single(porId.Agendamentos).Estado);
        Assert.Equal(EstadoConsultaAgendamentos.Multiplos, ambiguoLimitado.Estado);
        Assert.Equal([primeiro, segundo], ambiguoLimitado.Agendamentos.Select(item => item.Id));
        Assert.True(ambiguoLimitado.MaisResultados);
    }

    [PostgresFact]
    public async Task DisponibilidadeConsideraSomenteAgendadosEPermiteAdjacencia()
    {
        await InitializeAsync();
        var cliente = await SeedClienteAsync("Maria", "11999999999", null);
        var ativo = await SeedAgendamentoAsync(
            cliente,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Agendado,
            ModalidadeServico.Interno,
            240,
            UnidadeDuracao.Minutos,
            [Periodo(2030, 1, 7, 11, 15)]);
        await SeedAgendamentoAsync(
            cliente,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Cancelado,
            ModalidadeServico.Interno,
            120,
            UnidadeDuracao.Minutos,
            [Periodo(2030, 1, 7, 17, 19)]);
        await SeedAgendamentoAsync(
            cliente,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Concluido,
            ModalidadeServico.Interno,
            60,
            UnidadeDuracao.Minutos,
            [Periodo(2030, 1, 7, 19, 20)]);
        await using var db = PostgresTestDatabase.CreateContext();
        var application = new DisponibilidadeAgendaApplication(new PostgresConsultaAgendaStore(db));

        var conflito = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(10, 0),
            60,
            UnidadeDuracao.Minutos));
        var adjacente = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(12, 0),
            120,
            UnidadeDuracao.Minutos));
        var sobreCancelado = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(14, 0),
            120,
            UnidadeDuracao.Minutos));

        Assert.Equal(EstadoDisponibilidadeAgenda.Indisponivel, conflito.Estado);
        Assert.Equal([ativo], conflito.AgendamentosConflitantes);
        Assert.Equal(EstadoDisponibilidadeAgenda.Disponivel, adjacente.Estado);
        Assert.Equal(EstadoDisponibilidadeAgenda.Disponivel, sobreCancelado.Estado);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [PostgresFact]
    public async Task ConsultaEncontraPeriodoNaViradaDeMesEAnoPelaDataEfetiva()
    {
        await InitializeAsync();
        var cliente = await SeedClienteAsync("Maria", "11999999999", null);
        var agendamento = await SeedAgendamentoAsync(
            cliente,
            TiposServicoPredefinidos.VisitaTecnicaId,
            EstadoAgendamento.Agendado,
            ModalidadeServico.Interno,
            240,
            UnidadeDuracao.Minutos,
            [
                Periodo(2029, 12, 31, 19, 21),
                Periodo(2030, 1, 1, 11, 13)
            ]);
        await using var db = PostgresTestDatabase.CreateContext();
        var application = new ConsultaAgendaApplication(new PostgresConsultaAgendaStore(db));

        var janeiro = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2030, 1, 1),
            DataFinal: new DateOnly(2030, 1, 1)));

        var item = Assert.Single(janeiro.Agendamentos);
        Assert.Equal(agendamento, item.Id);
        var periodo = Assert.Single(item.Periodos);
        Assert.Equal(new DateOnly(2030, 1, 1), periodo.Data);
        Assert.Equal(new TimeOnly(8, 0), periodo.Inicio);
        Assert.Equal(new TimeOnly(10, 0), periodo.Fim);
    }

    private static async Task InitializeAsync()
    {
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.Database.MigrateAsync();
    }

    private static async Task<long> SeedClienteAsync(
        string nome,
        string telefone,
        string? endereco)
    {
        await using var db = PostgresTestDatabase.CreateContext();
        var cliente = new Cliente
        {
            Nome = nome,
            TelefoneNormalizado = telefone,
            EnderecoPrincipal = endereco
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Id;
    }

    private static async Task<long> SeedAgendamentoAsync(
        long clienteId,
        long tipoServicoId,
        EstadoAgendamento estado,
        ModalidadeServico modalidade,
        int duracao,
        UnidadeDuracao unidade,
        IReadOnlyList<PeriodoSeed> periodos,
        string? endereco = null)
    {
        await using var db = PostgresTestDatabase.CreateContext();
        var agendamento = new Agendamento
        {
            ClienteId = clienteId,
            TipoServicoId = tipoServicoId,
            Modalidade = modalidade,
            Duracao = duracao,
            UnidadeDuracao = unidade,
            InicioPrevistoUtc = periodos[0].Inicio,
            FimPrevistoUtc = periodos[^1].Fim,
            EnderecoSnapshot = endereco,
            Estado = estado
        };
        db.Agendamentos.Add(agendamento);
        await db.SaveChangesAsync();
        db.PeriodosOcupacao.AddRange(periodos.Select(periodo => new PeriodoOcupacao
        {
            AgendamentoId = agendamento.Id,
            InicioUtc = periodo.Inicio,
            FimUtc = periodo.Fim
        }));
        await db.SaveChangesAsync();
        return agendamento.Id;
    }

    private static PeriodoSeed Periodo(
        int ano,
        int mes,
        int dia,
        int horaInicioUtc,
        int horaFimUtc) =>
        new(
            new DateTimeOffset(ano, mes, dia, horaInicioUtc, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(ano, mes, dia, horaFimUtc, 0, 0, TimeSpan.Zero));

    private sealed record PeriodoSeed(DateTimeOffset Inicio, DateTimeOffset Fim);
}
