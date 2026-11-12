using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed record ConsultaAgendamentosFiltro(
    DateOnly? DataInicial = null,
    DateOnly? DataFinal = null,
    long? ClienteId = null,
    long? TipoServicoId = null,
    EstadoAgendamento? Estado = null,
    long? AgendamentoId = null,
    int Limite = 20);

public enum EstadoConsultaAgendamentos
{
    Nenhum,
    Unico,
    Multiplos,
    Invalido
}

public enum ErroConsultaAgenda
{
    DataFinalSemInicial,
    IntervaloDatasInvalido,
    DataFinalForaDoLimite,
    ClienteIdInvalido,
    TipoServicoIdInvalido,
    AgendamentoIdInvalido,
    EstadoInvalido,
    LimiteInvalido
}

public sealed record PeriodoAgendaItem(
    DateOnly Data,
    TimeOnly Inicio,
    TimeOnly Fim);

public sealed record AgendamentoConsultaItem(
    long Id,
    long ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    long TipoServicoId,
    string TipoServicoNome,
    ModalidadeServico Modalidade,
    int Duracao,
    UnidadeDuracao UnidadeDuracao,
    IReadOnlyList<PeriodoAgendaItem> Periodos,
    string? EnderecoServico,
    string? Observacoes,
    EstadoAgendamento Estado,
    long Versao = 1);

public sealed record ResultadoConsultaAgendamentos(
    EstadoConsultaAgendamentos Estado,
    IReadOnlyList<AgendamentoConsultaItem> Agendamentos,
    bool MaisResultados,
    ErroConsultaAgenda? Erro = null);

public interface IConsultaAgenda
{
    Task<ResultadoConsultaAgendamentos> ConsultarAsync(
        ConsultaAgendamentosFiltro filtro,
        CancellationToken cancellationToken = default);
}

public sealed record VerificarDisponibilidadeSolicitacao(
    DateOnly Data,
    TimeOnly? Horario,
    int Duracao,
    UnidadeDuracao UnidadeDuracao);

public enum EstadoDisponibilidadeAgenda
{
    Disponivel,
    Indisponivel,
    RequerEsclarecimento,
    Invalido
}

public sealed record ResultadoDisponibilidadeAgenda(
    EstadoDisponibilidadeAgenda Estado,
    IReadOnlyList<PeriodoAgendaItem> Periodos,
    IReadOnlyList<long> AgendamentosConflitantes,
    Ian.Domain.Agenda.MotivoCalculoOcupacao? Motivo = null);

public interface IDisponibilidadeAgenda
{
    Task<ResultadoDisponibilidadeAgenda> VerificarAsync(
        VerificarDisponibilidadeSolicitacao solicitacao,
        CancellationToken cancellationToken = default);
}

public sealed record ConsultaAgendaStoreFiltro(
    DateTimeOffset? InicioUtc,
    DateTimeOffset? FimUtc,
    long? ClienteId,
    long? TipoServicoId,
    EstadoAgendamento? Estado,
    long? AgendamentoId,
    int Limite);

public sealed record PeriodoAgendaStoreItem(
    DateTimeOffset InicioUtc,
    DateTimeOffset FimUtc);

public sealed record AgendamentoAgendaStoreItem(
    long Id,
    long ClienteId,
    string ClienteNome,
    string ClienteTelefone,
    long TipoServicoId,
    string TipoServicoNome,
    ModalidadeServico Modalidade,
    int Duracao,
    UnidadeDuracao UnidadeDuracao,
    IReadOnlyList<PeriodoAgendaStoreItem> Periodos,
    string? EnderecoServico,
    string? Observacoes,
    EstadoAgendamento Estado,
    long Versao = 1);

public interface IConsultaAgendaStore
{
    Task<IReadOnlyList<AgendamentoAgendaStoreItem>> ConsultarAsync(
        ConsultaAgendaStoreFiltro filtro,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<long>> ObterConflitosAsync(
        IReadOnlyList<PeriodoAgendaStoreItem> periodos,
        CancellationToken cancellationToken = default);
}
