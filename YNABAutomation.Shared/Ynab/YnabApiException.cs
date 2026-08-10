using System.Net;

namespace YNABAutomationConsole.Ynab;

public sealed class YnabApiException : Exception
{
    public YnabApiException(HttpStatusCode statusCode, ErrorDetail? error)
        : base(error?.Detail ?? $"The YNAB API returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        Error = error;
    }

    public HttpStatusCode StatusCode { get; }

    public ErrorDetail? Error { get; }
}
