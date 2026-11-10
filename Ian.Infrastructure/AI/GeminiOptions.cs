namespace Ian.Infrastructure.AI;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ApiKey) && ApiKey.Length <= 4096 &&
        !ApiKey.Any(char.IsWhiteSpace) && !ApiKey.Any(char.IsControl) &&
        Model.Length is > 0 and <= 128 &&
        Model.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') &&
        TimeoutSeconds is >= 1 and <= 300;
}
