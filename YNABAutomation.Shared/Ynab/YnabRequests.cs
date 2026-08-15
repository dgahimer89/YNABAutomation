using System.Globalization;
using System.Text.Json.Serialization;

namespace YNABAutomationConsole.Ynab;

public sealed class GetPlansOptions
{
    public bool? IncludeAccounts { get; init; }
}

public sealed class GetTransactionsOptions
{
    public DateOnly? SinceDate { get; init; }

    public DateOnly? UntilDate { get; init; }

    public TransactionType? Type { get; init; }

    public long? LastKnowledgeOfServer { get; init; }

    internal void Validate()
    {
        if (SinceDate.HasValue && UntilDate.HasValue && SinceDate > UntilDate)
        {
            throw new ArgumentException("SinceDate must be on or before UntilDate.");
        }

        if (LastKnowledgeOfServer is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(LastKnowledgeOfServer));
        }
    }
}

public enum TransactionType
{
    Uncategorized,
    Unapproved
}

public sealed class UpdateTransactionCategoryRequest
{
    public UpdateTransactionCategoryRequest(string transactionId, Guid? categoryId, bool? approved = null, Guid? payeeId = null)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException("A transaction ID is required.", nameof(transactionId));
        }

        TransactionId = transactionId;
        CategoryId = categoryId;
        Approved = approved;
        PayeeId = payeeId;
    }

    public string TransactionId { get; }

    public Guid? CategoryId { get; }

    public bool? Approved { get; }

    public Guid? PayeeId { get; }
}

public sealed class UpdateTransactionByImportIdRequest
{
    public UpdateTransactionByImportIdRequest(string importId, Guid? categoryId)
    {
        if (string.IsNullOrWhiteSpace(importId))
        {
            throw new ArgumentException("An import ID is required.", nameof(importId));
        }

        ImportId = importId;
        CategoryId = categoryId;
    }

    public string ImportId { get; }

    public Guid? CategoryId { get; }
}

public sealed class UpdateTransactionsRequest
{
    private UpdateTransactionsRequest(
        string? id,
        string? importId,
        Guid? categoryId,
        bool? approved = null,
        Guid? payeeId = null)
    {
        if (string.IsNullOrWhiteSpace(id) == string.IsNullOrWhiteSpace(importId))
        {
            throw new ArgumentException("Specify exactly one of id or importId.");
        }

        Id = id;
        ImportId = importId;
        CategoryId = categoryId;
        Approved = approved;
        PayeeId = payeeId;
    }

    public string? Id { get; }

    public string? ImportId { get; }

    public Guid? CategoryId { get; }

    public bool? Approved { get; }

    public Guid? PayeeId { get; }

    public static UpdateTransactionsRequest ById(string id, Guid? categoryId, bool? approved = null, Guid? payeeId = null) =>
        new(id, null, categoryId, approved, payeeId);

    public static UpdateTransactionsRequest ByImportId(string importId, Guid? categoryId, bool? approved = null, Guid? payeeId = null) =>
        new(null, importId, categoryId, approved, payeeId);
}

internal sealed class PatchTransactionsBody
{
    [JsonPropertyName("transactions")]
    public required IReadOnlyList<PatchTransaction> Transactions { get; init; }
}

internal sealed class PatchTransaction
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("import_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImportId { get; init; }

    [JsonPropertyName("category_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? CategoryId { get; init; }

    [JsonPropertyName("approved")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Approved { get; init; }

    [JsonPropertyName("payee_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? PayeeId { get; init; }
}

internal sealed class PutTransactionBody
{
    [JsonPropertyName("transaction")]
    public required PutTransaction Transaction { get; init; }
}

internal sealed class PutTransaction
{
    [JsonPropertyName("category_id")]
    public Guid? CategoryId { get; init; }

    [JsonPropertyName("approved")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Approved { get; init; }

    [JsonPropertyName("payee_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? PayeeId { get; init; }
}

internal static class YnabRequestFormatting
{
    public static string FormatDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatTransactionType(TransactionType type) => type switch
    {
        TransactionType.Uncategorized => "uncategorized",
        TransactionType.Unapproved => "unapproved",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
