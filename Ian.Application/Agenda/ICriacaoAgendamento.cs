using Ian.Application.Clientes;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public sealed record CriarAgendamentoSolicitacao(
    long UpdateId,
    CadastrarClienteSolicitacao Cliente,
    long TipoServicoId,
    DateOnly Data,
    TimeOnly? Horario,
    ModalidadeServico? Modalidade = null,
    int? Duracao = null,
    UnidadeDuracao? UnidadeDuracao = null,
    string? EnderecoServico = null,
    string? Observacoes = null);

public enum EstadoCriacaoAgendamento
{
    Criado,
    Reentrega,
    DadosInvalidos,
    RequerEsclarecimento,
    Conflito,
    AgendaIndisponivel,
    Falha
}

public enum ErroCriacaoAgendamento
{
    UpdateIdInvalido,
    ClienteInvalido,
    TipoServicoInvalido,
    TipoServicoNaoEncontrado,
    ModalidadeInvalida,
    UnidadeDuracaoInvalida,
    EnderecoExternoObrigatorio,
    CalculoInvalido,
    ResultadoIdempotenteAusente
}

public sealed record ResultadoCriacaoAgendamento(
    EstadoCriacaoAgendamento Estado,
    long? AgendamentoId,
    long? ClienteId,
    bool ClienteCriado,
    IReadOnlyList<PeriodoCalculado> Periodos,
    ErroCriacaoAgendamento? Erro = null,
    MotivoCalculoOcupacao? MotivoCalculo = null);

public sealed record IdentificacaoAgendamento(long AgendamentoId, long ClienteId);

public interface ICriacaoAgendamento
{
    Task<ResultadoCriacaoAgendamento> CriarAsync(
        CriarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default);
}

public interface IAgendaCriacaoStore
{
    Task<bool> ExisteConflitoAsync(
        IReadOnlyList<PeriodoCalculado> periodos,
        CancellationToken cancellationToken = default);

    Task<IdentificacaoAgendamento> CriarAsync(
        Agendamento agendamento,
        IReadOnlyList<PeriodoCalculado> periodos,
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default);

    Task<IdentificacaoAgendamento?> ObterPorOperacaoAsync(
        long updateId,
        string operationKey,
        CancellationToken cancellationToken = default);
}

public interface IAgendaMutationExecutor
{
    Task<bool> ExecuteOnceAsync(
        long updateId,
        string operationKey,
        Func<CancellationToken, Task> mutation,
        CancellationToken cancellationToken = default);
}

public sealed class AgendaVersionConflictException()
    : Exception("O agendamento foi alterado. Releia os dados e revalide a operação.");

public sealed class AgendaLockUnavailableException()
    : Exception("A agenda está ocupada. A operação não foi confirmada.");
