using System.Text.RegularExpressions;
using Ian.Application.Catalogo;

namespace Ian.Application.AI;

// Exact catalog names only; normalization never creates an alias or an ID.
public sealed record ScheduleServiceResolution(string Category, IReadOnlyList<TipoServicoCatalogoItem> Matches, string RemainingText)
{
    public static ScheduleServiceResolution Resolve(string text, IReadOnlyList<TipoServicoCatalogoItem> catalog)
    {
        var normalized = PendingTurnDataCapture.Normalize(text).Trim(' ', '.', ',', '!');
        if (Regex.IsMatch(normalized, @"\b(nao|talvez|qual|quais|consulta|consulte|consultar|cancelar)\b|[?]", RegexOptions.CultureInvariant))
            return new("NotApplicable", [], text);
        var matches = catalog.Where(t => Regex.IsMatch(normalized,
            @"(?<!\p{L})" + Regex.Escape(PendingTurnDataCapture.Normalize(t.Nome)) + @"(?!\p{L})", RegexOptions.CultureInvariant)).ToArray();
        if (matches.Length < 2 && Regex.IsMatch(normalized, @"\bou\b", RegexOptions.CultureInvariant))
            return new("NotApplicable", [], text);
        var rest = normalized;
        foreach (var name in matches.Select(t => PendingTurnDataCapture.Normalize(t.Nome)).Distinct())
            rest = Regex.Replace(rest, @"(?<!\p{L})" + Regex.Escape(name) + @"(?!\p{L})", " ", RegexOptions.CultureInvariant);
        rest = Regex.Replace(rest, @"\b(?:o servico|servico|desejado|sera|quero|uma|um|de|e)\b", " ", RegexOptions.CultureInvariant);
        return new(matches.Length switch { 0 => "Unrecognized", 1 => "Unique", _ => "Ambiguous" }, matches, rest);
    }
}
