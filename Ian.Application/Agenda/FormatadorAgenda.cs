using System.Globalization;
using System.Text;
using Ian.Domain.Entities;

namespace Ian.Application.Agenda;

public static class FormatadorAgenda
{
    public static string Formatar(
        DateOnly dataInicial,
        DateOnly dataFinal,
        IReadOnlyList<AgendamentoConsultaItem> agendamentos)
    {
        ArgumentNullException.ThrowIfNull(agendamentos);
        if (dataInicial > dataFinal)
        {
            throw new ArgumentException("A data inicial deve ser anterior ou igual à data final.");
        }

        var linhas = agendamentos
            .SelectMany(agendamento => agendamento.Periodos
                .Where(periodo => periodo.Data >= dataInicial && periodo.Data <= dataFinal)
                .Select(periodo => new LinhaAgenda(agendamento, periodo)))
            .OrderBy(linha => linha.Periodo.Data)
            .ThenBy(linha => linha.Periodo.Inicio)
            .ThenBy(linha => linha.Agendamento.Id)
            .ToArray();

        var texto = new StringBuilder();
        if (dataInicial == dataFinal)
        {
            texto.Append("Agenda de ")
                .Append(FormatarData(dataInicial));
        }
        else
        {
            texto.Append("Agenda de ")
                .Append(FormatarData(dataInicial))
                .Append(" a ")
                .Append(FormatarData(dataFinal));
        }

        if (linhas.Length == 0)
        {
            return texto.AppendLine().AppendLine()
                .Append("Nenhum agendamento encontrado.")
                .ToString();
        }

        DateOnly? dataAtual = null;
        foreach (var linha in linhas)
        {
            if (dataInicial != dataFinal && linha.Periodo.Data != dataAtual)
            {
                texto.AppendLine().AppendLine()
                    .Append(FormatarData(linha.Periodo.Data));
                dataAtual = linha.Periodo.Data;
            }

            texto.AppendLine().AppendLine()
                .Append(FormatarHora(linha.Periodo.Inicio))
                .Append('–')
                .Append(FormatarHora(linha.Periodo.Fim))
                .Append(" | ")
                .Append(linha.Agendamento.TipoServicoNome)
                .Append(" — ")
                .Append(linha.Agendamento.ClienteNome)
                .Append(" [#")
                .Append(linha.Agendamento.Id.ToString(CultureInfo.InvariantCulture))
                .Append("] | ")
                .Append(linha.Agendamento.Estado == EstadoAgendamento.Concluido
                    ? "Concluído" : linha.Agendamento.Estado.ToString());
        }

        return texto.ToString();
    }

    private static string FormatarData(DateOnly data) =>
        data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatarHora(TimeOnly hora) =>
        hora.Minute == 0
            ? hora.ToString("HH'h'", CultureInfo.InvariantCulture)
            : hora.ToString("HH'h'mm", CultureInfo.InvariantCulture);

    private sealed record LinhaAgenda(
        AgendamentoConsultaItem Agendamento,
        PeriodoAgendaItem Periodo);
}
