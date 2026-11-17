using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ian.Application.Agenda;
using Ian.Application.Conversation;
using Ian.Domain.Entities;

namespace Ian.Application.AI;

// Durable references contain no phone, address, transcript or model-generated identity.
public sealed record AppointmentReference(long Id, long Version, long ClienteId, long TipoServicoId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EstadoAgendamento>))] EstadoAgendamento Estado)
{
    public static AppointmentReference From(AgendamentoConsultaItem item) =>
        new(item.Id, item.Versao, item.ClienteId, item.TipoServicoId, item.Estado);
}

public sealed record AgentOperationalContext(DateTimeOffset ExpiresAtUtc, long? ClienteId,
    AppointmentReference? SelectedAppointment, IReadOnlyList<AppointmentReference> Appointments, bool MoreAppointments = false)
{
    public static AgentOperationalContext Empty(DateTimeOffset now) => new(now.Add(PendingConversationService.Lifetime), null, null, []);
    public AgentOperationalContext WithQuery(ResultadoConsultaAgendamentos result, DateTimeOffset now)
    {
        var items = result.Agendamentos.Take(10).Select(AppointmentReference.From).ToArray();
        var selected = items.Length == 1 && !result.MaisResultados ? items[0] : null;
        return this with { ExpiresAtUtc = now.Add(PendingConversationService.Lifetime), Appointments = items,
            SelectedAppointment = selected, MoreAppointments = result.MaisResultados,
            ClienteId = selected?.ClienteId ?? ClienteId };
    }
}

public sealed record PendingOperationContext([property: JsonConverter(typeof(JsonStringEnumConverter<PendingIntent>))] PendingIntent Intent, AgentToolRequest? Request,
    IReadOnlyList<string> CollectedFields, IReadOnlyList<string> MissingFields,
    IReadOnlyDictionary<string, IReadOnlyList<long>> Candidates, bool ConfirmationPending);

public static partial class AgentContextProjection
{
    public static PendingOperationContext? Pending(PendingConversationState? pending, bool confirmationPending)
    {
        if (pending is null) return null;
        var request = pending.CollectedFields.GetValueOrDefault("request") is { } json ? AgentRequestJson.Deserialize(json) : null;
        var fields = request is null ? [] : JsonSerializer.SerializeToElement(request, AgentRequestJson.Options)
            .EnumerateObject().Where(p => p.Name != "$type" && p.Value.ValueKind != JsonValueKind.Null).Select(p => p.Name).ToArray();
        var safe = request switch
        {
            CriarAgendamentoToolRequest r => r with { TelefoneCliente = null, EnderecoServico = null, Observacoes = null },
            AlterarAgendamentoToolRequest r => r with { EnderecoServico = null, Observacoes = null },
            CadastrarClienteToolRequest r => r with { Telefone = null, EnderecoPrincipal = null },
            LocalizarClienteToolRequest r => r with { Telefone = null },
            _ => request
        };
        return new(pending.Intent, safe, fields, pending.MissingFields, pending.CandidateIds, confirmationPending);
    }

    public static string Redact(string? text, int limit) => text is null ? "" : Limit(Address().Replace(
        Phone().Replace(Code().Replace(text, "[confirmação protegida]"), "[telefone informado]"), "[endereço informado]"), limit);
    private static string Limit(string value, int limit) => value.Length <= limit ? value : value[..limit];
    [GeneratedRegex(@"(?<!\d)(?:\+55\s*)?(?:\([1-9]\d\)|[1-9]\d)\s*[2-9]\d{3,4}[ -]?\d{4}(?!\d)")]
    private static partial Regex Phone();
    [GeneratedRegex(@"\b(?:endere[cç]o:|rua\s|avenida\s|travessa\s|alameda\s)[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Address();
    [GeneratedRegex(@"\bconfirmar\s+[a-fA-F0-9-]{32,36}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Code();
}
