using System.Text.RegularExpressions;
using System.Globalization;
using Ian.Application.Conversation;
using Ian.Domain.Clientes;

namespace Ian.Application.AI;

/// <summary>Only unambiguous replies to the persisted next field are captured without the model.</summary>
public static partial class PendingTurnDataCapture
{
    public static CriarAgendamentoToolRequest? Capture(string text, PendingConversationState pending)
    {
        if (pending.Intent != PendingIntent.CriarAgendamento || pending.CandidateIds.Count > 0 ||
            !pending.CollectedFields.TryGetValue("request", out var json) ||
            AgentRequestJson.Deserialize(json) is not CriarAgendamentoToolRequest current) return null;
        var next = pending.MissingFields.FirstOrDefault() ?? "";
        if (next.StartsWith("nome", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(current.NomeCliente) && IsDirectName(text))
            return Empty() with { NomeCliente = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) };
        if (next.StartsWith("telefone", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(current.TelefoneCliente))
        {
            var phone = NormalizadorTelefoneBrasileiro.Normalizar(text);
            if (phone.Valido) return Empty() with { TelefoneCliente = phone.TelefoneNormalizado };
        }
        return null;
    }

    public static bool IsInvalidPhoneReply(string text, PendingConversationState pending) =>
        pending.Intent == PendingIntent.CriarAgendamento && pending.CandidateIds.Count == 0 &&
        (pending.MissingFields.FirstOrDefault()?.StartsWith("telefone", StringComparison.OrdinalIgnoreCase) ?? false) &&
        PhoneShape().IsMatch(text) && !NormalizadorTelefoneBrasileiro.Normalizar(text).Valido;

    public static bool IsDirectName(string text) => text.Trim().Length is > 0 and <= 200 && NameShape().IsMatch(text.Trim()) &&
        !NonName().IsMatch(text) && !IsSimpleGreeting(text);

    public static CriarAgendamentoToolRequest InitialSchedule(string text, DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        var today = DateOnly.FromDateTime(local.DateTime);
        var hours = Hour().Matches(text);
        TimeOnly? time = hours.Count == 1 ? new TimeOnly(int.Parse(hours[0].Groups["hour"].Value),
            hours[0].Groups["minute"].Success ? int.Parse(hours[0].Groups["minute"].Value) : 0) : null;
        var days = Day().Matches(text);
        var dates = ExplicitDate().Matches(text);
        DateOnly? date = dates.Count == 1 && days.Count == 0 && DateOnly.TryParseExact(dates[0].Value,
            ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var explicitDate) ? explicitDate : null;
        if (days.Count == 1 && dates.Count == 0)
        {
            var day = days[0].Value.ToLowerInvariant();
            if (day == "hoje") date = today;
            else if (day is "amanhã" or "amanha") date = today.AddDays(1);
            else
            {
                var target = day switch
                {
                    "domingo" => DayOfWeek.Sunday, "segunda" => DayOfWeek.Monday, "terça" or "terca" => DayOfWeek.Tuesday,
                    "quarta" => DayOfWeek.Wednesday, "quinta" => DayOfWeek.Thursday, "sexta" => DayOfWeek.Friday, _ => DayOfWeek.Saturday
                };
                var offset = ((int)target - (int)today.DayOfWeek + 7) % 7;
                if (offset == 0 && time is { } requested && requested <= TimeOnly.FromDateTime(local.DateTime)) offset = 7;
                date = today.AddDays(offset);
            }
        }
        // Never silently move an explicit "today" to another day or infer a future hour.
        if (date == today && time <= TimeOnly.FromDateTime(local.DateTime)) time = null;
        return Empty() with { Data = date, Horario = time };
    }

    public static bool IsSimpleGreeting(string text) => Greeting().IsMatch(text);
    public static bool IsCatalogRequest(string text) => CatalogRequest().IsMatch(text);
    private static CriarAgendamentoToolRequest Empty() => new(null, null, null, null, null, null, null, null);

    [GeneratedRegex(@"^[\p{L}][\p{L}'’\-]*(?:\s+[\p{L}][\p{L}'’\-]*){0,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameShape();
    [GeneratedRegex(@"\b(oi|olá|ola|bom|boa|dia|tarde|noite|sim|não|nao|tudo|bem|quero|queria|gostaria|preciso|agendar|marcar|cancelar|consultar|consulte|consulta|agenda|serviço|servicos|serviços|telefone|cliente|agendamento|hoje|amanhã|amanha|segunda|terça|terca|quarta|quinta|sexta|sábado|sabado|domingo|descartar|confirmar|confirmo|id|tá|ta|aí|ai|como|vai|ajuda|ajude|pode|continuar|mostre|qual|quais|listar|editar|reagendar|concluir|orçamento)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonName();
    [GeneratedRegex(@"^[+\d\s()\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneShape();
    [GeneratedRegex(@"\b(?:às|as)\s*(?<hour>[01]?\d|2[0-3])(?:(?:h|:)(?<minute>[0-5]\d)?)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hour();
    [GeneratedRegex(@"\b(?:\d{1,2}/\d{1,2}/\d{4}|\d{4}-\d{2}-\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitDate();
    [GeneratedRegex(@"\b(hoje|amanhã|amanha|domingo|segunda|terça|terca|quarta|quinta|sexta|sábado|sabado)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Day();
    [GeneratedRegex(@"^\s*(?:(?:oi|olá|ola|bom dia|boa tarde|boa noite)(?:[\s,!]+(?:ian|ia\.?n))?(?:[\s,!]+(?:tudo bem|como vai))?|(?:tá|ta|está|esta)\s+a[ií]|tudo bem)[\s!?.]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Greeting();
    [GeneratedRegex(@"\b(catálogo|catalogo)\b|\b(quais|liste|listar|lista|opções)\b.{0,35}\b(serviços|servicos|serviço|servico)\b|\bnão sei qual serviço\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogRequest();
}
