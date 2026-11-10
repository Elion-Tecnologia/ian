namespace Ian.Application.AI;

public sealed record AiModelResult
{
    private AiModelResult(string? text, AiModelFailure? failure, AiModelUsage? usage)
    {
        Text = text;
        Failure = failure;
        Usage = usage;
    }

    public string? Text { get; }
    public AiModelFailure? Failure { get; }
    public AiModelUsage? Usage { get; }
    public bool IsSuccess => Failure is null;

    public string? ErrorMessage => Failure switch
    {
        null => null,
        AiModelFailure.InvalidPrompt => "O prompt deve conter texto e respeitar o limite de 16000 caracteres.",
        AiModelFailure.InvalidConfiguration =>
            "Configuração Gemini inválida. Verifique Gemini:ApiKey, Gemini:Model e Gemini:TimeoutSeconds.",
        AiModelFailure.AuthenticationFailed => "O provedor recusou a autenticação.",
        AiModelFailure.QuotaExceeded => "A quota ou o limite de requisições do provedor foi excedido.",
        AiModelFailure.Timeout => "O tempo limite da invocação do modelo foi excedido.",
        AiModelFailure.ProviderUnavailable => "O provedor está temporariamente indisponível.",
        AiModelFailure.ModelUnavailable => "O modelo configurado não está disponível para esta solicitação.",
        AiModelFailure.ProviderRejected => "O provedor recusou a solicitação.",
        AiModelFailure.InvalidResponse => "O modelo não retornou uma resposta textual válida e completa.",
        _ => "Ocorreu uma falha inesperada na integração com o modelo."
    };

    public static AiModelResult Succeeded(string text, AiModelUsage? usage = null) =>
        string.IsNullOrWhiteSpace(text)
            ? Failed(AiModelFailure.InvalidResponse)
            : new(text.Trim(), null, usage);

    public static AiModelResult Failed(AiModelFailure failure) => new(null, failure, null);

    // Avoid accidental transcript disclosure through diagnostic interpolation.
    public override string ToString() => Failure?.ToString() ?? "Success";
}

public sealed record AiModelUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);
