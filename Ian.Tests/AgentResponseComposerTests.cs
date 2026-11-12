using Ian.Application.AI;

namespace Ian.Tests;

public sealed class AgentResponseComposerTests
{
    [Fact]
    public void RedacaoEscolhidaUsaSomentePerguntaConfiavelSemPerderStatus()
    {
        var result = AgentToolResult.Missing("Qual é o telefone com DDD do cliente?") with
        {
            RedacoesPermitidas = ["Pode me passar o telefone do cliente com DDD?"]
        };
        var composed = AgentResponseComposer.Compose(result, "Vamos por partes. Pode me passar o telefone do cliente com DDD?", true);
        Assert.Equal(result.Outcome, composed.Outcome);
        Assert.StartsWith("Vamos por partes.", composed.Message);
        Assert.Contains("telefone", composed.Message);
        Assert.DoesNotContain("nome", composed.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PRIVATE: Agendamento criado para amanhã às 10h.")]
    [InlineData("Pode informar o telefone e confirmar o cancelamento?")]
    [InlineData("Qual é o telefone?\0")]
    public void RedacaoNaoComprovadaUsaFallback(string? draft)
    {
        var trusted = AgentToolResult.Missing("Qual é o telefone com DDD do cliente?");
        Assert.Same(trusted, AgentResponseComposer.Compose(trusted, draft, true));
    }

    [Fact]
    public void LimiteENovaSaudacaoUsamFallback()
    {
        var trusted = AgentToolResult.Missing("Como posso ajudar com sua agenda?");
        Assert.Same(trusted, AgentResponseComposer.Compose(trusted, new string('x', 4001), false));
        Assert.Same(trusted, AgentResponseComposer.Compose(trusted, "Olá! " + trusted.Message, true));
        Assert.StartsWith("Olá!", AgentResponseComposer.Compose(trusted, "Olá! " + trusted.Message, false).Message);
    }

    [Fact]
    public void FatosCodigosERecusasPrecisamPermanecerIntegrais()
    {
        AgentToolResult[] results =
        [
            AgentToolResult.Succeeded("Agendamento criado: Montagem para Ana, 07/01/2030. 08h–18h. Rua A."),
            AgentToolResult.ConflictResult("Não consegui agendar: há conflito. Escolha outra data."),
            AgentToolResult.Ambiguity("Selecione: 1 Ana; 2 Ana B. Envie cliente <ID>."),
            AgentToolResult.RequiresConfirmation("Confirma o cancelamento do agendamento 2? Envie confirmar abc123."),
            AgentToolResult.Failed("PRIVATE_PROVIDER_ERROR")
        ];
        foreach (var result in results)
        {
            Assert.Same(result, AgentResponseComposer.Compose(result, "Tudo certo, agendei!", false));
            Assert.Same(result, AgentResponseComposer.Compose(result, result.Message.Replace("2030", "2031").Replace("abc123", "abc124") + " Sim basta.", true));
            var composed = AgentResponseComposer.Compose(result, "Sobre seu pedido:\n" + result.Message, true);
            Assert.Equal(result.Outcome, composed.Outcome);
            Assert.Contains(result.Message, composed.Message);
            Assert.Equal(result.Data, composed.Data);
        }
    }

    [Theory]
    [InlineData("{malformed}")]
    [InlineData("{\"objetivo\":\"Continuar\",\"resposta\":\"Coletar\",\"texto\":{}}")]
    [InlineData("{\"objetivo\":123,\"resposta\":\"Coletar\"}")]
    public void ContratoMalformadoNaoViraRedacao(string json) => Assert.Null(AgentDialoguePlan.Parse(json));
}
