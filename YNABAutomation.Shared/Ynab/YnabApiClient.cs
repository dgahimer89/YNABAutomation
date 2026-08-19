using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YNABAutomationConsole.Ynab;

public sealed class YnabApiClient(
    HttpClient httpClient,
    IOptions<YnabOptions> options,
    ILogger<YnabApiClient>? logger = null) : IYnabApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly YnabOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<YnabApiClient> _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YnabApiClient>.Instance;

    public async Task<PlansResponse> GetPlansAsync(
        GetPlansOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting YNAB plans.");
        var query = options?.IncludeAccounts is bool includeAccounts
            ? $"?include_accounts={includeAccounts.ToString().ToLowerInvariant()}"
            : string.Empty;

        using var response = await _httpClient.GetAsync($"plans{query}", cancellationToken);
        return await ReadResponseAsync<PlansResponse>(response, cancellationToken);
    }

    public async Task<TransactionsResponse> GetTransactionsAsync(
        GetTransactionsOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting YNAB transactions. Type={Type}.", options?.Type);
        options ??= new GetTransactionsOptions();
        options.Validate();

        var query = new StringBuilder();
        AddQuery(query, "since_date", options.SinceDate is DateOnly sinceDate
            ? YnabRequestFormatting.FormatDate(sinceDate)
            : null);
        AddQuery(query, "until_date", options.UntilDate is DateOnly untilDate
            ? YnabRequestFormatting.FormatDate(untilDate)
            : null);
        AddQuery(query, "type", options.Type is TransactionType type
            ? YnabRequestFormatting.FormatTransactionType(type)
            : null);
        AddQuery(query, "last_knowledge_of_server", options.LastKnowledgeOfServer?.ToString());

        var planId = await GetPlanIdAsync(cancellationToken);
        using var response = await _httpClient.GetAsync(
            $"plans/{planId}/transactions{query}", cancellationToken);
        return await ReadResponseAsync<TransactionsResponse>(response, cancellationToken);
    }

    public async Task<TransactionResponse> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        _logger.LogInformation("Requesting YNAB transaction {TransactionId}.", transactionId);

        var planId = await GetPlanIdAsync(cancellationToken);
        using var response = await _httpClient.GetAsync(
            $"plans/{planId}/transactions/{Uri.EscapeDataString(transactionId)}",
            cancellationToken);
        return await ReadResponseAsync<TransactionResponse>(response, cancellationToken);
    }

    public async Task<CategoriesResponse> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Requesting YNAB categories.");
        var planId = await GetPlanIdAsync(cancellationToken);
        using var response = await _httpClient.GetAsync(
            $"plans/{planId}/categories?last_knowledge_of_server=0", cancellationToken);
        return await ReadResponseAsync<CategoriesResponse>(response, cancellationToken);
    }

    public async Task<SaveTransactionsResponse> UpdateTransactionsAsync(
        IReadOnlyCollection<UpdateTransactionsRequest> transactions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        if (transactions.Count == 0)
        {
            throw new ArgumentException("At least one transaction is required.", nameof(transactions));
        }
        _logger.LogInformation("Updating {Count} YNAB transactions.", transactions.Count);

        var body = new PatchTransactionsBody
        {
            Transactions = transactions.Select(transaction => new PatchTransaction
            {
                Id = transaction.Id,
                ImportId = transaction.ImportId,
                CategoryId = transaction.CategoryId,
                Approved = transaction.Approved,
                PayeeId = transaction.PayeeId
            }).ToArray()
        };

        var planId = await GetPlanIdAsync(cancellationToken);
        using var response = await _httpClient.PatchAsJsonAsync(
            $"plans/{planId}/transactions", body, JsonOptions, cancellationToken);
        return await ReadResponseAsync<SaveTransactionsResponse>(response, cancellationToken);
    }

    public async Task<TransactionResponse> UpdateTransactionAsync(
        UpdateTransactionCategoryRequest transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        _logger.LogInformation("Updating YNAB transaction {TransactionId}.", transaction.TransactionId);

        var body = new PutTransactionBody
        {
            Transaction = new PutTransaction
            {
                CategoryId = transaction.CategoryId,
                Approved = transaction.Approved,
                PayeeId = transaction.PayeeId
            }
        };

        var planId = await GetPlanIdAsync(cancellationToken);
        using var response = await _httpClient.PutAsJsonAsync(
            $"plans/{planId}/transactions/{Uri.EscapeDataString(transaction.TransactionId)}",
            body,
            JsonOptions,
            cancellationToken);
        return await ReadResponseAsync<TransactionResponse>(response, cancellationToken);
    }

    private string? _resolvedPlanId;

    private async Task<string> GetPlanIdAsync(CancellationToken cancellationToken)
    {
        var configuredPlanId = _options.PlanId;
        if (!string.IsNullOrWhiteSpace(configuredPlanId))
        {
            return Uri.EscapeDataString(configuredPlanId);
        }

        if (_resolvedPlanId is null)
        {
            _logger.LogInformation("Discovering the YNAB plan ID.");
            var plans = await GetPlansAsync(cancellationToken: cancellationToken);
            if (plans.Data.Plans.Count != 1)
            {
                throw new InvalidOperationException(
                    "A plan ID is required when the authenticated user does not have exactly one plan.");
            }

            _resolvedPlanId = Uri.EscapeDataString(plans.Data.Plans[0].Id.ToString());
        }

        return _resolvedPlanId!;
    }

    private async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                JsonOptions, cancellationToken);
            throw new YnabApiException(response.StatusCode, error?.Error);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new YnabApiException(
            response.StatusCode,
            new ErrorDetail
            {
                Id = "invalid_response",
                Name = "InvalidResponse",
                Detail = "The YNAB API returned an empty response body."
            });
    }

    private static void AddQuery(StringBuilder query, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        query.Append(query.Length == 0 ? '?' : '&');
        query.Append(Uri.EscapeDataString(name));
        query.Append('=');
        query.Append(Uri.EscapeDataString(value));
    }
}
