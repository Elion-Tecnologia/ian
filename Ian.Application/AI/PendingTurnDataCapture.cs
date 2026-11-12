using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using Ian.Application.Conversation;
using Ian.Domain.Clientes;

namespace Ian.Application.AI;

/// <summary>Captures explicit scheduling fields without making a model inference.</summary>
public static partial class PendingTurnDataCapture
{
    public static CriarAgendamentoToolRequest? Capture(string text, PendingConversationState pending, DateTimeOffset? utcNow = null)
    {
        if (pending.Intent != PendingIntent.CriarAgendamento || pending.CandidateIds.Count > 0 ||
            !pending.CollectedFields.TryGetValue("request", out var json) ||
            AgentRequestJson.Deserialize(json) is not CriarAgendamentoToolRequest) return null;
        var input = Extract(text, utcNow ?? DateTimeOffset.UtcNow,
            pending.MissingFields.FirstOrDefault()?.StartsWith("nome", StringComparison.OrdinalIgnoreCase) == true);
        return input.HasFields ? input.Request : null;
    }

    // Remaining text is kept so collecting a phone never swallows an address or another command.
    public static ScheduleInput Extract(string text, DateTimeOffset utcNow, bool expectingName = false)
    {
        if (IsNonCollectionInput(text)) return new(Empty(), text);
        var schedule = InitialSchedule(text, utcNow);
        var rest = text;
        if (schedule.Data is not null) rest = ExplicitDate().Replace(DayPhrase().Replace(rest, " "), " ");
        if (schedule.Horario is not null) rest = Hour().Replace(rest, " ");
        var phones = Phone().Matches(rest);
        string? phone = null;
        if (phones.Count == 1)
        {
            var normalized = NormalizadorTelefoneBrasileiro.Normalizar(phones[0].Value);
            if (normalized.Valido)
            {
                phone = normalized.TelefoneNormalizado;
                rest = rest.Remove(phones[0].Index, phones[0].Length).Insert(phones[0].Index, " ");
            }
        }
        string? name = null;
        var labeled = LabeledName().Match(rest);
        var prefix = rest.Split(['.', ',', ';'], 2)[0].Trim();
        if (labeled.Success && IsDirectName(labeled.Groups["name"].Value.Trim()))
        {
            name = labeled.Groups["name"].Value.Trim();
            rest = rest.Remove(labeled.Index, labeled.Length).Insert(labeled.Index, " ");
        }
        else if (IsDirectName(prefix) && (expectingName || (prefix.Contains(' ') && (schedule.Data is not null || phone is not null))))
        {
            name = prefix;
            rest = rest.Remove(rest.IndexOf(prefix, StringComparison.Ordinal), prefix.Length);
        }
        return new(schedule with { NomeCliente = name, TelefoneCliente = phone }, rest);
    }

    public static bool IsNonCollectionInput(string text) => UncertainOrQuery().IsMatch(text) || IsSimpleGreeting(text);

    public static bool IsCollectionRemainder(string text) =>
        string.IsNullOrWhiteSpace(Fillers().Replace(text, " ").Trim(' ', '.', ',', ';', ':', '!', '-'));

    public static bool IsExplicitIdentityChange(string text) => IdentityChange().IsMatch(text);
    public static string Normalize(string text) => string.Join(" ", new string(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
        if (Regex.IsMatch(text, @"\b(tarde|noite)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return Empty();
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
                if (offset == 0 && (Regex.IsMatch(text, @"\bpr[oó]xim[oa]\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                    time is { } requested && requested <= TimeOnly.FromDateTime(local.DateTime))) offset = 7;
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

    [GeneratedRegex(@"\b(?:ou|talvez|se|não|nao|qual|quais|consultar|consulte|consulta|agenda|tenho|mostre|liste|listar|verifique|verificar|disponibilidade|cancelar|cancele)\b|[?]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UncertainOrQuery();
    [GeneratedRegex(@"(?<![\d/])(?:\+55\s*)?(?:\([1-9]\d\)|[1-9]\d)\s*[2-9]\d{3,4}[ -]?\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();
    [GeneratedRegex(@"(?:\b(?:na|no|em|para)\s+)?(?:pr[oó]xim[oa]\s+)?\b(?:hoje|amanhã|amanha|domingo|segunda|terça|terca|quarta|quinta|sexta|sábado|sabado)(?:-feira)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayPhrase();
    [GeneratedRegex(@"\b(?:meu nome [ée]|nome(?: do cliente)?(?: [ée])?[: ]|cliente:)\s*(?<name>[\p{L}][\p{L}'’\-]*(?:\s+[\p{L}][\p{L}'’\-]*){0,5})(?=[.,;]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabeledName();
    [GeneratedRegex(@"\b(?:vai ser|o início|o inicio|início|inicio|o telefone é|o telefone e|o telefone|telefone|celular|com ddd|ddd|pode começar|pode comecar|pode iniciar|começando|comecando|para|em|e|da manhã|da manha)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Fillers();
    [GeneratedRegex(@"\b(?:troque|trocar|mude|mudar|corrija|corrigir|na verdade|o nome correto|o telefone correto)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdentityChange();
    [GeneratedRegex(@"^[\p{L}][\p{L}'’\-]*(?:\s+[\p{L}][\p{L}'’\-]*){0,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameShape();
    [GeneratedRegex(@"\b(oi|olá|ola|bom|boa|dia|tarde|noite|sim|não|nao|tudo|bem|quero|queria|gostaria|preciso|agendar|marcar|cancelar|consultar|consulte|consulta|agenda|serviço|servicos|serviços|telefone|cliente|agendamento|hoje|amanhã|amanha|segunda|terça|terca|quarta|quinta|sexta|sábado|sabado|domingo|descartar|confirmar|confirmo|id|tá|ta|aí|ai|como|vai|ajuda|ajude|pode|continuar|mostre|qual|quais|listar|editar|reagendar|concluir|orçamento|orcamento|montagem|visita|ajuste|obrigad[oa]|valeu|é|eh|para|endereço|endereco|troque|trocar|mude|corrija)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonName();
    [GeneratedRegex(@"^[+\d\s()\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneShape();
    [GeneratedRegex(@"\b(?:às|as)\s*(?<hour>[01]?\d|2[0-3])(?:h(?<minute>[0-5]\d)?|:(?<minute>[0-5]\d))?(?![\d:hH])\b(?:\s*(?:da|de)\s+manh[ãa])?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
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

public sealed record ScheduleInput(CriarAgendamentoToolRequest Request, string RemainingText)
{
    public bool HasFields => Request.NomeCliente is not null || Request.TelefoneCliente is not null || Request.Data is not null || Request.Horario is not null;
    public bool IsCompleteReply => PendingTurnDataCapture.IsCollectionRemainder(RemainingText);
}
