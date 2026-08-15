using System.ClientModel;
using System.Text.Json;
using OpenAI.Responses;

namespace YNABAutomationConsole.Categorization;

#pragma warning disable OPENAI001

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-5-mini";
    public decimal AutoApplyConfidenceThreshold { get; set; } = 0.95m;
    public int MaximumHistoricalObservations { get; set; } = 5;
}

public sealed record AiCategory(Guid Id, string Name, string GroupName);
public sealed record AiHistoricalObservation(Guid CategoryId, string CategoryName, int Count);

public sealed record AiCategorizationRequest(
    string? OriginalPayee,
    string NormalizedPayee,
    DateOnly TransactionDate,
    long Amount,
    TransactionDirection Direction,
    string? AccountName,
    string? Memo,
    IReadOnlyList<AiHistoricalObservation> History,
    IReadOnlyList<AiCategory> AllowedCategories);

public sealed record AiCategorizationResult(
    string? CategoryId,
    decimal Confidence,
    string Reason,
    string? AlternativeCategoryId,
    bool RequiresReview);

public interface IAiCategorizer
{
    bool IsConfigured { get; }
    Task<AiCategorizationResult> CategorizeAsync(
        AiCategorizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledAiCategorizer : IAiCategorizer
{
    public bool IsConfigured => false;

    public Task<AiCategorizationResult> CategorizeAsync(
        AiCategorizationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("OpenAI is not configured.");
}

public sealed class OpenAiCategorizer(OpenAiOptions options) : IAiCategorizer
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "categoryId": { "type": ["string", "null"] },
            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
            "reason": { "type": "string", "maxLength": 300 },
            "alternativeCategoryId": { "type": ["string", "null"] },
            "requiresReview": { "type": "boolean" }
          },
          "required": ["categoryId", "confidence", "reason", "alternativeCategoryId", "requiresReview"]
        }
        """;

    private readonly ResponsesClient _client = new(options.ApiKey
        ?? throw new ArgumentException("OpenAI API key is required.", nameof(options)));

    public bool IsConfigured => true;

    public async Task<AiCategorizationResult> CategorizeAsync(
        AiCategorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = CreatePayload(request);
        var options = CreateResponseOptions(payload);
        var response = await _client.CreateResponseAsync(options, cancellationToken);
        var outputText = ExtractOutputText(response.Value);
        return DeserializeResult(outputText);
    }

    private static string CreatePayload(AiCategorizationRequest request) =>
        JsonSerializer.Serialize(new
        {
            originalPayee = request.OriginalPayee,
            normalizedPayee = request.NormalizedPayee,
            transactionDate = request.TransactionDate,
            amountMilliunits = request.Amount,
            direction = request.Direction.ToString(),
            accountName = request.AccountName,
            memo = request.Memo,
            history = request.History,
            allowedCategories = request.AllowedCategories
        });

    private CreateResponseOptions CreateResponseOptions(string payload) =>
        new(
            options.Model,
            [ResponseItem.CreateUserMessageItem(payload)])
        {
            Instructions = "Categorize this YNAB transaction. Choose categoryId and alternativeCategoryId only from allowedCategories. Categorize inflows and outflows. Consider history when present. Set requiresReview true instead of guessing when ambiguous. Give a concise reason and confidence from 0 to 1. Return no prose outside the schema.",
            MaxOutputTokenCount = 20_000,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "ynab_categorization",
                    BinaryData.FromString(Schema),
                    "A constrained YNAB categorization decision.",
                    true)
            }
        };

    private static string ExtractOutputText(ResponseResult response)
    {
        var outputText = response.GetOutputText();
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            return outputText;
        }

        var diagnostics = new List<string>
        {
            $"status={response.Status}"
        };
        if (response.IncompleteStatusDetails?.Reason is { } incompleteReason)
        {
            diagnostics.Add($"incompleteReason={incompleteReason}");
        }

        if (response.Error is { } error)
        {
            diagnostics.Add($"errorCode={error.Code}");
            diagnostics.Add($"errorKind={error.Kind}");
            if (!string.IsNullOrWhiteSpace(error.Param))
            {
                diagnostics.Add($"errorParam={error.Param}");
            }

            if (!string.IsNullOrWhiteSpace(error.Message))
            {
                diagnostics.Add($"errorMessage={error.Message}");
            }
        }

        diagnostics.Add($"outputItemCount={response.OutputItems.Count}");
        throw new InvalidDataException($"OpenAI returned no text output ({string.Join(", ", diagnostics)}).");
    }

    private static AiCategorizationResult DeserializeResult(string? outputText)
    {
        var result = JsonSerializer.Deserialize<AiCategorizationResult>(
            outputText,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? throw new InvalidDataException("OpenAI returned an empty categorization result.");
    }
}
