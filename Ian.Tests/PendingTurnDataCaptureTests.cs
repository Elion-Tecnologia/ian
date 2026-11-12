using Ian.Application.AI;
using Ian.Application.Conversation;

namespace Ian.Tests;

public sealed class PendingTurnDataCaptureTests
{
    [Theory]
    [InlineData("Gabriel Ramos")]
    [InlineData("gabriel ramos")]
    [InlineData("Ana")]
    [InlineData("João da Silva")]
    public void NomeDiretoPreencheSomenteNomePendente(string name)
    {
        var capture = PendingTurnDataCapture.Capture(name, Pending("nomedocliente"));
        Assert.Equal(name, capture?.NomeCliente);
        Assert.Null(capture?.TelefoneCliente);
        Assert.Null(PendingTurnDataCapture.Capture(name, Pending("servicodesejado")));
    }

    [Theory]
    [InlineData("Oi IAN, tudo bem?")]
    [InlineData("Olá")]
    [InlineData("Tá aí?")]
    [InlineData("Quero consultar agenda")]
    [InlineData("Quero cancelar")]
    [InlineData("confirmar abc123")]
    [InlineData("/descartar")]
    [InlineData("cliente 1")]
    [InlineData("1")]
    [InlineData("Gabriel Ramos?")]
    public void MensagemNaoInequivocaNaoViraNome(string text) =>
        Assert.Null(PendingTurnDataCapture.Capture(text, Pending("nomedocliente")));

    [Theory]
    [InlineData("13997209208")]
    [InlineData("(13) 99720-9208")]
    [InlineData("+55 (13) 99720-9208")]
    public void TelefoneIsoladoUsaNormalizadorExistente(string text)
    {
        var pending = Pending("telefonecomDDD", "Gabriel Ramos");
        Assert.Equal("13997209208", PendingTurnDataCapture.Capture(text, pending)?.TelefoneCliente);
        Assert.Equal("13997209208", PendingTurnDataCapture.Capture(text, Pending("nomedocliente"))?.TelefoneCliente);
        Assert.Null(PendingTurnDataCapture.Capture(text, pending with { Intent = PendingIntent.AlterarAgendamento }));
        Assert.Null(PendingTurnDataCapture.Capture(text, pending with
            { CandidateIds = new Dictionary<string, IReadOnlyList<long>> { ["cliente"] = [1, 2] } }));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("139")]
    [InlineData("00997209208")]
    public void TelefoneInvalidoNaoAvanca(string text)
    {
        var pending = Pending("telefonecomDDD", "Gabriel Ramos");
        Assert.Null(PendingTurnDataCapture.Capture(text, pending));
        Assert.True(PendingTurnDataCapture.IsInvalidPhoneReply(text, pending));
    }

    [Theory]
    [InlineData("2026-11-13T01:30:00Z", "2026-11-16")]
    [InlineData("2026-11-16T11:30:00Z", "2026-11-16")]
    [InlineData("2026-11-16T12:30:00Z", "2026-11-23")]
    public void SegundaAsNoveUsaSaoPauloEHorarioFuturo(string instant, string day)
    {
        var result = PendingTurnDataCapture.InitialSchedule("Eu queria agendar um serviço para segunda-feira de manhã às 9h", DateTimeOffset.Parse(instant));
        Assert.Equal(DateOnly.Parse(day), result.Data);
        Assert.Equal(new TimeOnly(9, 0), result.Horario);
        Assert.Null(result.TipoServicoId);
        Assert.Null(result.Duracao);
    }

    [Fact]
    public void CapturaSomenteDataHoraInequivocasSemInventarHorario()
    {
        var now = DateTimeOffset.Parse("2026-11-12T15:00:00Z");
        var explicitDate = PendingTurnDataCapture.InitialSchedule("Agende em 16/11/2026 às 09:30", now);
        Assert.Equal(new DateOnly(2026, 11, 16), explicitDate.Data);
        Assert.Equal(new TimeOnly(9, 30), explicitDate.Horario);
        Assert.Null(PendingTurnDataCapture.InitialSchedule("hoje às 9h", now).Horario);
        Assert.Null(PendingTurnDataCapture.InitialSchedule("segunda ou terça às 9h", now).Data);
        Assert.Null(PendingTurnDataCapture.InitialSchedule("segunda às 9h ou às 10h", now).Horario);
    }

    [Theory]
    [InlineData("Oi IAN, tudo bem?", true)]
    [InlineData("Olá!", true)]
    [InlineData("Oi, quero agendar", false)]
    [InlineData("Bom dia, Gabriel", false)]
    public void SaudacaoCompletaNaoConfundeDadosOuIntencao(string text, bool expected) =>
        Assert.Equal(expected, PendingTurnDataCapture.IsSimpleGreeting(text));

    private static PendingConversationState Pending(string missing, string? name = null) => new(1, 42, PendingIntent.CriarAgendamento,
        new Dictionary<string, string> { ["request"] = AgentRequestJson.Serialize(new CriarAgendamentoToolRequest(name, null, null,
            new DateOnly(2026, 11, 16), new TimeOnly(9, 0), null, null, null)) },
        [missing], new Dictionary<string, IReadOnlyList<long>>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
}
