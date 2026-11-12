using Ian.Application.Agenda;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class ConsultaAgendaApplicationTests
{
    [Fact]
    public async Task DatasLocaisSaoConvertidasParaLimitesUtcDeSaoPaulo()
    {
        var store = new ConsultaStoreFake
        {
            Resultados =
            [
                Registro(1, new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero))
            ]
        };
        var application = new ConsultaAgendaApplication(store);

        var resultado = await application.ConsultarAsync(new(
            DataInicial: new DateOnly(2026, 11, 12)));

        Assert.NotNull(store.UltimoFiltro);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 12, 3, 0, 0, TimeSpan.Zero),
            store.UltimoFiltro.InicioUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 3, 0, 0, TimeSpan.Zero),
            store.UltimoFiltro.FimUtc);
        Assert.Equal(EstadoConsultaAgendamentos.Unico, resultado.Estado);
        var periodo = Assert.Single(Assert.Single(resultado.Agendamentos).Periodos);
        Assert.Equal(new DateOnly(2026, 11, 12), periodo.Data);
        Assert.Equal(new TimeOnly(8, 0), periodo.Inicio);
        Assert.Equal(new TimeOnly(12, 0), periodo.Fim);
    }

    [Fact]
    public async Task LimiteDistingueMultiplosEMaisResultados()
    {
        var store = new ConsultaStoreFake
        {
            Resultados = [Registro(1), Registro(2), Registro(3)]
        };
        var application = new ConsultaAgendaApplication(store);

        var resultado = await application.ConsultarAsync(new(Limite: 2));

        Assert.Equal(EstadoConsultaAgendamentos.Multiplos, resultado.Estado);
        Assert.Equal([1L, 2L], resultado.Agendamentos.Select(item => item.Id));
        Assert.True(resultado.MaisResultados);
        Assert.Equal(3, store.UltimoFiltro?.Limite);
    }

    [Fact]
    public async Task FiltrosInvalidosNaoConsultamPersistencia()
    {
        var store = new ConsultaStoreFake();
        var application = new ConsultaAgendaApplication(store);
        var filtros = new[]
        {
            new ConsultaAgendamentosFiltro(DataFinal: new DateOnly(2030, 1, 1)),
            new ConsultaAgendamentosFiltro(
                DataInicial: new DateOnly(2030, 1, 2),
                DataFinal: new DateOnly(2030, 1, 1)),
            new ConsultaAgendamentosFiltro(DataInicial: DateOnly.MaxValue),
            new ConsultaAgendamentosFiltro(ClienteId: 0),
            new ConsultaAgendamentosFiltro(TipoServicoId: -1),
            new ConsultaAgendamentosFiltro(AgendamentoId: 0),
            new ConsultaAgendamentosFiltro(Estado: (EstadoAgendamento)999),
            new ConsultaAgendamentosFiltro(Limite: 0)
        };

        foreach (var filtro in filtros)
        {
            var resultado = await application.ConsultarAsync(filtro);
            Assert.Equal(EstadoConsultaAgendamentos.Invalido, resultado.Estado);
        }

        Assert.Equal(0, store.Consultas);
    }

    [Fact]
    public async Task DisponibilidadeDistingueConflitoAdjacenciaInvalidadeEEsclarecimento()
    {
        var store = new ConsultaStoreFake { Conflitos = [42] };
        var application = new DisponibilidadeAgendaApplication(store);

        var indisponivel = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(10, 0),
            60,
            UnidadeDuracao.Minutos));
        store.Conflitos = [];
        var disponivel = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(12, 0),
            120,
            UnidadeDuracao.Minutos));
        var invalido = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(18, 0),
            60,
            UnidadeDuracao.Minutos));
        var esclarecimento = await application.VerificarAsync(new(
            new DateOnly(2030, 1, 7),
            new TimeOnly(10, 0),
            1,
            UnidadeDuracao.DiasCorridos));

        Assert.Equal(EstadoDisponibilidadeAgenda.Indisponivel, indisponivel.Estado);
        Assert.Equal([42L], indisponivel.AgendamentosConflitantes);
        Assert.Equal(EstadoDisponibilidadeAgenda.Disponivel, disponivel.Estado);
        Assert.Equal(EstadoDisponibilidadeAgenda.Invalido, invalido.Estado);
        Assert.Equal(MotivoCalculoOcupacao.InicioForaDoExpediente, invalido.Motivo);
        Assert.Equal(EstadoDisponibilidadeAgenda.RequerEsclarecimento, esclarecimento.Estado);
        Assert.Equal(2, store.ConsultasConflito);
    }

    [Fact]
    public void FormatacaoEhDeterministicaLegivelEIncluiIdsParaDesambiguacao()
    {
        var itens = new[]
        {
            Item(2, "João", new DateOnly(2026, 11, 12), new TimeOnly(14, 0), new TimeOnly(18, 0)),
            Item(1, "Maria", new DateOnly(2026, 11, 12), new TimeOnly(8, 0), new TimeOnly(12, 0))
        };

        var texto = FormatadorAgenda.Formatar(
            new DateOnly(2026, 11, 12),
            new DateOnly(2026, 11, 12),
            itens);
        var novaLinha = Environment.NewLine;

        Assert.Equal(
            $"Agenda de 12/11/2026{novaLinha}{novaLinha}" +
            $"08h–12h | Visita técnica — Maria [#1] | Agendado{novaLinha}{novaLinha}" +
            "14h–18h | Visita técnica — João [#2] | Agendado",
            texto);
    }

    private static AgendamentoAgendaStoreItem Registro(
        long id,
        DateTimeOffset? inicio = null,
        DateTimeOffset? fim = null) =>
        new(
            id,
            10,
            "Maria",
            "11999999999",
            TiposServicoPredefinidos.VisitaTecnicaId,
            TiposServicoPredefinidos.VisitaTecnicaNome,
            ModalidadeServico.Interno,
            60,
            UnidadeDuracao.Minutos,
            [
                new(
                    inicio ?? new DateTimeOffset(2030, 1, 7, 11, 0, 0, TimeSpan.Zero),
                    fim ?? new DateTimeOffset(2030, 1, 7, 12, 0, 0, TimeSpan.Zero))
            ],
            null,
            null,
            EstadoAgendamento.Agendado);

    private static AgendamentoConsultaItem Item(
        long id,
        string cliente,
        DateOnly data,
        TimeOnly inicio,
        TimeOnly fim) =>
        new(
            id,
            id,
            cliente,
            "11999999999",
            1,
            "Visita técnica",
            ModalidadeServico.Interno,
            60,
            UnidadeDuracao.Minutos,
            [new(data, inicio, fim)],
            null,
            null,
            EstadoAgendamento.Agendado);

    private sealed class ConsultaStoreFake : IConsultaAgendaStore
    {
        public IReadOnlyList<AgendamentoAgendaStoreItem> Resultados { get; init; } = [];
        public IReadOnlyList<long> Conflitos { get; set; } = [];
        public ConsultaAgendaStoreFiltro? UltimoFiltro { get; private set; }
        public int Consultas { get; private set; }
        public int ConsultasConflito { get; private set; }

        public Task<IReadOnlyList<AgendamentoAgendaStoreItem>> ConsultarAsync(
            ConsultaAgendaStoreFiltro filtro,
            CancellationToken cancellationToken = default)
        {
            Consultas++;
            UltimoFiltro = filtro;
            return Task.FromResult(Resultados);
        }

        public Task<IReadOnlyList<long>> ObterConflitosAsync(
            IReadOnlyList<PeriodoAgendaStoreItem> periodos,
            CancellationToken cancellationToken = default)
        {
            ConsultasConflito++;
            return Task.FromResult(Conflitos);
        }
    }
}
