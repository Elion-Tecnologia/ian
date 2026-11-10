namespace Ian.Application.AI;

public interface IAiModel
{
    // The caller must authorize the request before invoking the model.
    // A successful response is text, never authorization to mutate business data.
    Task<AiModelResult> InvokeAsync(string prompt, CancellationToken cancellationToken = default);
}
