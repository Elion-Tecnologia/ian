using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class OperationConfirmationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly AlterarAgendamentoToolRequest Cancel = new(1, "cancelar", null, null, null);

    [Theory]
    [InlineData("cancelar", true)]
    [InlineData("CANCELAR", true)]
    [InlineData("reagendar", false)]
    [InlineData("concluir", false)]
    [InlineData("editar", false)]
    public void OnlySensitiveOperationsRequireConfirmation(string operation, bool expected) =>
        Assert.Equal(expected, OperationConfirmationPolicy.RequiresConfirmation(Cancel with { Operacao = operation }));

    [Fact]
    public void AuthorizationRequiresTheConfiguredAdministratorAndPrivateHumanText()
    {
        var caller = new AgentToolCaller(42, 42, true, true, true);
        Assert.True(OperationConfirmationPolicy.IsAuthorized(caller, 42));
        foreach (var denied in new[] { caller with { UserId = 43 }, caller with { ChatId = -42 },
                     caller with { IsPrivateChat = false }, caller with { IsHuman = false }, caller with { HasText = false } })
            Assert.False(OperationConfirmationPolicy.IsAuthorized(denied, 42));
        Assert.False(OperationConfirmationPolicy.IsAuthorized(caller, 0));
    }

    [Fact]
    public void AmbiguousOrMissingDataMustBeResolvedBeforeConfirmation()
    {
        var pending = Pending();
        pending.CandidatosJson = "{\"agendamento\":[1,2]}";
        Assert.Equal(AgentToolOutcome.Ambiguous, Validate(pending)!.Outcome);
        pending.CandidatosJson = "{}";
        pending.CamposFaltantesJson = "[\"agendamentoId\"]";
        Assert.Equal(AgentToolOutcome.MissingData, Validate(pending)!.Outcome);
        pending.CamposFaltantesJson = "[]";
        Assert.Null(Validate(pending));
        Assert.Equal(AgentToolOutcome.Conflict,
            OperationConfirmationPolicy.ValidatePending(pending, 42, Cancel with { AgendamentoId = 2 }, Now)!.Outcome);
    }

    [Fact]
    public void ClosedReplacedExpiredOrOtherActorsRequestsCannotBeConfirmed()
    {
        foreach (var state in new[] { EstadoSolicitacaoPendente.Cancelada, EstadoSolicitacaoPendente.Confirmada, EstadoSolicitacaoPendente.Expirada })
        {
            var pending = Pending();
            pending.Estado = state;
            Assert.NotNull(Validate(pending));
        }
        Assert.NotNull(OperationConfirmationPolicy.ValidatePending(Pending(), 43, Cancel, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidatePending(Pending(), 42, Cancel, Now.AddMinutes(30)));
        Assert.NotNull(OperationConfirmationPolicy.ValidatePending(Pending(), 42, Cancel with { Observacoes = "outro argumento" }, Now));
    }

    [Fact]
    public void AcceptanceBindsChallengeActorTargetVersionArgumentsStateAndDeadline()
    {
        var token = Guid.NewGuid();
        var stored = new StoredConfirmation(token, 1, 1, Cancel, Now.AddMinutes(5));
        var acceptance = new ConfirmationAcceptance(10, token, 1, Cancel);
        var target = Target();
        Assert.Null(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance, target, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance with { ChallengeId = Guid.NewGuid() }, target, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance with { Version = 2 }, target, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance with { Arguments = Cancel with { AgendamentoId = 2 } }, target, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance, null, Now));
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance, target, stored.ExpiresAtUtc));
        target.IncrementarVersao();
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance, target, Now));
        target = Target();
        target.Estado = EstadoAgendamento.Cancelado;
        Assert.NotNull(OperationConfirmationPolicy.ValidateAcceptance(stored, acceptance, target, Now));
    }

    private static AgentToolResult? Validate(SolicitacaoPendente pending) =>
        OperationConfirmationPolicy.ValidatePending(pending, 42, Cancel, Now);

    private static SolicitacaoPendente Pending() => new()
    {
        Id = 10, TelegramUserId = 42, Operacao = nameof(PendingIntent.AlterarAgendamento),
        DadosColetadosJson = "{\"agendamentoId\":\"1\",\"operacao\":\"cancelar\"}",
        CriadaEmUtc = Now, AtualizadaEmUtc = Now, ExpiraEmUtc = Now.AddMinutes(30)
    };

    private static Agendamento Target() => new()
    {
        Id = 1, ClienteId = 1, TipoServicoId = 1, Modalidade = ModalidadeServico.Interno,
        Duracao = 60, UnidadeDuracao = UnidadeDuracao.Minutos, InicioPrevistoUtc = Now, FimPrevistoUtc = Now.AddHours(1)
    };
}
