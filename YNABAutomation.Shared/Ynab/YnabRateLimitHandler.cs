using Microsoft.Extensions.Logging;

namespace YNABAutomationConsole.Ynab;

public sealed class YnabRateLimitHandler(
    IYnabRequestRateLimiter limiter,
    ILogger<YnabRateLimitHandler>? logger = null,
    TimeSpan? rateLimitPause = null) : DelegatingHandler
{
    private static readonly TimeSpan DefaultRateLimitPause = TimeSpan.FromHours(1);
    private readonly IYnabRequestRateLimiter _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    private readonly ILogger<YnabRateLimitHandler> _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YnabRateLimitHandler>.Instance;
    private readonly TimeSpan _rateLimitPause = rateLimitPause ?? DefaultRateLimitPause;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await _limiter.WaitAsync(cancellationToken);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            response.Dispose();
            _logger.LogWarning(
                "YNAB returned HTTP 429 (too many requests). Pausing YNAB requests for {PauseMinutes} minutes before resuming.",
                _rateLimitPause.TotalMinutes);
            await _limiter.PauseAsync(_rateLimitPause, cancellationToken);
            _logger.LogInformation("YNAB rate-limit pause completed; resuming the request.");
            request = await CloneRequestAsync(request, cancellationToken);
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = new ByteArrayContent(
                await request.Content.ReadAsByteArrayAsync(cancellationToken));
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = content;
        }

        return clone;
    }
}
