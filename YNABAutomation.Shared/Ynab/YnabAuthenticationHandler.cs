using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace YNABAutomationConsole.Ynab;

public sealed class YnabAuthenticationHandler(IOptions<YnabOptions> options) : DelegatingHandler
{
    private readonly YnabOptions _options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("The YNAB API key is not configured.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
