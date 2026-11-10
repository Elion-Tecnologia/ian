namespace Ian.Application.AI;

public enum AiModelFailure
{
    InvalidPrompt,
    InvalidConfiguration,
    AuthenticationFailed,
    QuotaExceeded,
    Timeout,
    ProviderUnavailable,
    ModelUnavailable,
    ProviderRejected,
    InvalidResponse,
    UnexpectedFailure
}
