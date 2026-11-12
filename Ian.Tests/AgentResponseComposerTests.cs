using Ian.Application.AI;

namespace Ian.Tests;

public sealed class AgentResponseComposerTests
{
    [Theory]
    [InlineData("Oi! Que bom falar com você. Como posso ajudar?")]
    [InlineData("Tudo bem por aqui! E com você?")]
    [InlineData("Estou aqui, pode falar!")]
    public void ConversaAceitaRedacaoLivreSemIgualdadeLiteral(string text)
    {
        var trusted = AgentToolResult.Missing("Como posso ajudar?") with { DialogueMode = AgentDialogueMode.Social };
        Assert.Equal(text, AgentResponseComposer.Compose(trusted, text, true).Message);
    }

    [Theory]
    [InlineData("Me conta: qual é o melhor telefone de contato?")]
    [InlineData("Pode me passar o celular com DDD, por favor?")]
    public void ColetaAceitaParafrasesDoDadoAusente(string text)
    {
        var trusted = AgentToolResult.Missing("Qual é o telefone com DDD do cliente?") with
            { DialogueMode = AgentDialogueMode.Collection, QuestionTopic = "telefone" };
        var composed = AgentResponseComposer.Compose(trusted, text, true);
        Assert.Equal(text, composed.Message);
        Assert.Equal(AgentToolOutcome.MissingData, composed.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Agendei para amanhã às dez.")]
    [InlineData("Já está reservado, basta responder sim.")]
    [InlineData("O horário está livre.")]
    [InlineData("Você tem um serviço agendado.")]
    [InlineData("Pode confirmar o cancelamento?")]
    [InlineData("Qual o nome e o telefone?")]
    [InlineData("Qual é o endereço?")]
    [InlineData("Telefone 123456789.")]
    [InlineData("Qual é o telefone?\0")]
    public void TextoInseguroOuPerguntaSobreOutrosCamposUsaFallback(string? draft)
    {
        var trusted = AgentToolResult.Missing("Qual é o telefone com DDD?") with
            { DialogueMode = AgentDialogueMode.Collection, QuestionTopic = "telefone" };
        Assert.Same(trusted, AgentResponseComposer.Compose(trusted, draft, true));
    }

    [Fact]
    public void EsclarecimentoPreservaFatosObrigatoriosSemRestringirTextoLiteral()
    {
        var trusted = AgentToolResult.Missing("Qual é o telefone?") with
        {
            DialogueMode = AgentDialogueMode.Collection, QuestionTopic = "telefone",
            RequiredFacts = "Você pediu início às 10:00. O serviço exige início às 08h."
        };
        var result = AgentResponseComposer.Compose(trusted, "Me passa o telefone com DDD?", true);
        Assert.StartsWith("Me passa", result.Message);
        Assert.Contains(trusted.RequiredFacts, result.Message);
        var explanation = trusted with { DialogueMode = AgentDialogueMode.Explanation };
        var clarified = AgentResponseComposer.Compose(explanation, "Esse dado ajuda a identificar o cliente certo.", true);
        Assert.Contains(explanation.Message, clarified.Message);
        Assert.Contains(explanation.RequiredFacts!, clarified.Message);
    }

    [Fact]
    public void ResultadoSensivelNuncaUsaProsaDoModelo()
    {
        AgentToolResult[] results =
        [
            AgentToolResult.Succeeded("Agendamento criado: Ana, 07/01/2030, 08h–18h."),
            AgentToolResult.ConflictResult("Há conflito; escolha outro horário."),
            AgentToolResult.Ambiguity("1 Ana; 2 Ana B. Envie cliente <ID>."),
            AgentToolResult.RequiresConfirmation("Agendamento 2. Envie confirmar abc123."),
            AgentToolResult.Failed("PRIVATE_PROVIDER_ERROR")
        ];
        foreach (var result in results)
        {
            Assert.Same(result, AgentResponseComposer.Compose(result, "Tudo certo, fiz o que pediu!", false));
            Assert.Same(result, AgentResponseComposer.Compose(result, "Só responder sim.", true));
            Assert.Same(result, AgentResponseComposer.Compose(result, "Sobre seu pedido:\n" + result.Message, true));
        }
    }

    [Fact]
    public void RedacaoExcessivaUsaFallback()
    {
        var trusted = AgentToolResult.Missing("Como posso ajudar?") with { DialogueMode = AgentDialogueMode.Social };
        Assert.Same(trusted, AgentResponseComposer.Compose(trusted, new string('x', 4001), false));
    }

    [Theory]
    [InlineData("{malformed}")]
    [InlineData("{\"objetivo\":\"Continuar\",\"resposta\":\"Coletar\",\"texto\":{}}")]
    [InlineData("{\"objetivo\":123,\"resposta\":\"Coletar\"}")]
    public void ContratoMalformadoNaoViraRedacao(string json) => Assert.Null(AgentDialoguePlan.Parse(json));

    [Theory]
    [InlineData(AgentFailureCategory.ModelTimeout, "demorou")]
    [InlineData(AgentFailureCategory.ModelInvalidResponse, "reformular")]
    [InlineData(AgentFailureCategory.ModelFailure, "indisponível")]
    [InlineData(AgentFailureCategory.ToolTimeout, "Consulte a agenda")]
    [InlineData(AgentFailureCategory.ToolFailure, "Consulte a agenda")]
    public void ErrosSanitizadosSaoDistintosEUtilizaveis(AgentFailureCategory category, string expected)
    {
        var text = AgentToolResultTranslator.ToDialogue(AgentToolResult.Failed("PRIVATE_SECRET", category));
        Assert.Contains(expected, text);
        Assert.DoesNotContain("PRIVATE_SECRET", text);
    }
}
