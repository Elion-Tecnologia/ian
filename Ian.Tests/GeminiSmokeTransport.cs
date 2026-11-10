namespace Ian.Tests;

// Test-only transport boundary. Counts dispatches, never URLs/headers/bodies.
internal sealed class GeminiSmokeTransport(int maxCalls, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private int _calls;
    private int _blocked;

    public int Calls => Volatile.Read(ref _calls);
    public int Blocked => Volatile.Read(ref _blocked);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (request.Method != HttpMethod.Post || uri is null || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.Host != "generativelanguage.googleapis.com" ||
            !uri.AbsolutePath.EndsWith("/models/gemini-3.1-flash-lite:generateContent", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _blocked);
            throw new InvalidOperationException("Destino não autorizado no smoke.");
        }

        while (true)
        {
            var current = Calls;
            if (current >= maxCalls)
            {
                Interlocked.Increment(ref _blocked);
                throw new InvalidOperationException("Limite de chamadas do smoke atingido; não repetir.");
            }
            if (Interlocked.CompareExchange(ref _calls, current + 1, current) == current) break;
        }
        return base.SendAsync(request, cancellationToken);
    }
}
