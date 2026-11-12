using Ian.Domain.Agenda;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

// Null preserves the current value. The flags allow explicitly clearing nullable fields.
public sealed record EditarAgendamentoSolicitacao(
    long UpdateId,
    long AgendamentoId,
    long VersaoEsperada,
    DateOnly? Data = null,
    TimeOnly? Horario = null,
    long? TipoServicoId = null,
    ModalidadeServico? Modalidade = null,
    int? Duracao = null,
    UnidadeDuracao? UnidadeDuracao = null,
    string? EnderecoServico = null,
    string? Observacoes = null,
    bool AlterarEndereco = false,
    bool AlterarObservacoes = false);

public enum EstadoEdicaoAgendamento
{
    Editado,
    Reentrega,
    NaoEncontrado,
    EstadoNaoPermitido,
    VersaoObsoleta,
    DadosInvalidos,
    RequerEsclarecimento,
    Conflito,
    AgendaIndisponivel
}

public enum ErroEdicaoAgendamento
{
    IdentificacaoInvalida,
    TipoServicoNaoEncontrado,
    ModalidadeInvalida,
    UnidadeDuracaoInvalida,
    EnderecoExternoObrigatorio,
    CalculoInvalido
}

public sealed record ResultadoEdicaoAgendamento(
    EstadoEdicaoAgendamento Estado,
    long AgendamentoId,
    long? Versao = null,
    IReadOnlyList<PeriodoCalculado>? Periodos = null,
    ErroEdicaoAgendamento? Erro = null,
    MotivoCalculoOcupacao? MotivoCalculo = null);

public interface IEdicaoAgendamento
{
    Task<ResultadoEdicaoAgendamento> EditarAsync(
        EditarAgendamentoSolicitacao solicitacao,
        CancellationToken cancellationToken = default);
}

public interface IAgendaEdicaoStore
{
    Task<long?> ObterIdPorOperacaoAsync(long updateId, CancellationToken cancellationToken = default);

    // Called only inside the mutation executor, after the global lock.
    Task<Agendamento?> ObterAsync(long id, CancellationToken cancellationToken = default);

    Task SubstituirPeriodosAsync(
        Agendamento agendamento,
        IReadOnlyList<PeriodoCalculado> periodos,
        long updateId,
        CancellationToken cancellationToken = default);
}
