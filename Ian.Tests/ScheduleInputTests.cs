using Ian.Application.AI;
using Ian.Application.Catalogo;
using Ian.Domain.Entities;

namespace Ian.Tests;

public sealed class ScheduleInputTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-13T01:30:00Z");

    [Theory]
    [InlineData("Gabriel Ramos. Vai ser na próxima segunda-feira às 9h da manhã o início", "Gabriel Ramos", false)]
    [InlineData("GABRIEL RAMOS, telefone (13) 99720-9208, na PROXIMA SEGUNDA-feira às 9h da manha", "GABRIEL RAMOS", true)]
    [InlineData("Nome: Gabriel Ramos; 13997209208; 16/11/2026 às 09:00", "Gabriel Ramos", true)]
    public void ExtraiTodosOsDadosExplicitosSemModelo(string text, string name, bool phone)
    {
        var input = PendingTurnDataCapture.Extract(text, Now, expectingName: true);
        Assert.Equal(name, input.Request.NomeCliente);
        Assert.Equal(new DateOnly(2026, 11, 16), input.Request.Data);
        Assert.Equal(new TimeOnly(9, 0), input.Request.Horario);
        Assert.Equal(phone ? "13997209208" : null, input.Request.TelefoneCliente);
        Assert.True(input.IsCompleteReply, input.RemainingText);
    }

    [Theory]
    [InlineData("Talvez Gabriel Ramos, segunda ou terça às 9h")]
    [InlineData("Não agende na segunda às 9h")]
    [InlineData("Consulte segunda às 9h")]
    [InlineData("Agenda de amanhã")]
    [InlineData("Gabriel Ramos?")]
    [InlineData("Oi")]
    public void NaoTransformaHipoteseConsultaOuSaudacaoEmColeta(string text) =>
        Assert.False(PendingTurnDataCapture.Extract(text, Now, true).HasFields);

    [Fact]
    public void TelefoneNaoConsomeEnderecoAindaNaoInterpretado()
    {
        var result = PendingTurnDataCapture.Extract("Telefone 13997209208, Rua Fictícia", Now);
        Assert.Equal("13997209208", result.Request.TelefoneCliente);
        Assert.False(result.IsCompleteReply);
        Assert.Contains("Rua Fictícia", result.RemainingText);
    }

    [Theory]
    [InlineData("montagem", 912)]
    [InlineData("MONTAGEM!", 912)]
    [InlineData("Visita tecnica", 705)]
    [InlineData("VISITA TÉCNICA.", 705)]
    public void ResolvePeloNomeDoCatalogoSemIdFixado(string text, long expected)
    {
        TipoServicoCatalogoItem[] catalog = [new(912, "Montagem", 7, UnidadeDuracao.DiasCorridos, ModalidadeServico.Externo),
            new(705, "Visita técnica", 240, UnidadeDuracao.Minutos, ModalidadeServico.Externo)];
        var result = ScheduleServiceResolution.Resolve(text, catalog);
        Assert.Equal("Unique", result.Category);
        Assert.Equal(expected, Assert.Single(result.Matches).Id);
        Assert.True(PendingTurnDataCapture.IsCollectionRemainder(result.RemainingText));
    }

    [Theory]
    [InlineData("Manutenção", "Unrecognized")]
    [InlineData("Desmontagem", "Unrecognized")]
    [InlineData("Montagem ou Ajuste", "Ambiguous")]
    [InlineData("Não quero Montagem", "NotApplicable")]
    [InlineData("Montagem e Ajuste", "Ambiguous")]
    public void NaoInventaIdOuEscolheEntreServicos(string text, string category)
    {
        TipoServicoCatalogoItem[] catalog = [new(2, "Montagem", 7, UnidadeDuracao.DiasCorridos, ModalidadeServico.Externo),
            new(3, "Ajuste", 7, UnidadeDuracao.DiasCorridos, ModalidadeServico.Externo)];
        Assert.Equal(category, ScheduleServiceResolution.Resolve(text, catalog).Category);
    }

    [Theory]
    [InlineData("às 9:99")]
    [InlineData("às 29h")]
    [InlineData("às 9h99")]
    [InlineData("às 9h ou às 10h")]
    public void HorarioInvalidoOuAlternativoNaoViraHorarioParcial(string text) =>
        Assert.Null(PendingTurnDataCapture.Extract(text, Now).Request.Horario);

    [Fact]
    public void ProximaSegundaEHojeUsamDataLocalMesmoComUtcNoDiaSeguinte()
    {
        Assert.Equal(new DateOnly(2026, 11, 12), PendingTurnDataCapture.Extract("hoje", Now).Request.Data);
        Assert.Equal(new DateOnly(2026, 11, 23), PendingTurnDataCapture.Extract("próxima segunda às 9h",
            DateTimeOffset.Parse("2026-11-16T11:00:00Z")).Request.Data);
    }
}
