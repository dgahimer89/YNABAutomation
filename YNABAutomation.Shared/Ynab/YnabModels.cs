using System.Text.Json.Serialization;

namespace YNABAutomationConsole.Ynab;

public sealed class PlansResponse
{
    [JsonPropertyName("data")]
    public PlansData Data { get; init; } = new();
}

public sealed class PlansData
{
    [JsonPropertyName("plans")]
    public IReadOnlyList<PlanSummary> Plans { get; init; } = [];
}

public sealed class PlanSummary
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("last_modified_on")]
    public DateTimeOffset? LastModifiedOn { get; init; }

    [JsonPropertyName("is_external")]
    public bool IsExternal { get; init; }

    [JsonPropertyName("last_used")]
    public bool LastUsed { get; init; }

    [JsonPropertyName("date_format")]
    public DateFormat? DateFormat { get; init; }

    [JsonPropertyName("currency_format")]
    public CurrencyFormat? CurrencyFormat { get; init; }

    [JsonPropertyName("accounts")]
    public IReadOnlyList<Account> Accounts { get; init; } = [];
}

public sealed class DateFormat
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;
}

public sealed class CurrencyFormat
{
    [JsonPropertyName("iso_code")]
    public string? IsoCode { get; init; }

    [JsonPropertyName("example_format")]
    public string? ExampleFormat { get; init; }
}

public sealed class Account
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("on_budget")]
    public bool OnBudget { get; init; }

    [JsonPropertyName("closed")]
    public bool Closed { get; init; }

    [JsonPropertyName("balance")]
    public long Balance { get; init; }
}

public sealed class CategoriesResponse
{
    [JsonPropertyName("data")]
    public CategoriesData Data { get; init; } = new();
}

public sealed class CategoriesData
{
    [JsonPropertyName("category_groups")]
    public IReadOnlyList<CategoryGroup> CategoryGroups { get; init; } = [];

    [JsonPropertyName("server_knowledge")]
    public long ServerKnowledge { get; init; }
}

public sealed class CategoryGroup
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<Category> Categories { get; init; } = [];
}

public sealed class Category
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("category_group_id")]
    public Guid? CategoryGroupId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }

    [JsonPropertyName("budgeted")]
    public long Budgeted { get; init; }

    [JsonPropertyName("activity")]
    public long Activity { get; init; }

    [JsonPropertyName("balance")]
    public long Balance { get; init; }
}

public sealed class TransactionsResponse
{
    [JsonPropertyName("data")]
    public TransactionsData Data { get; init; } = new();
}

public sealed class TransactionsData
{
    [JsonPropertyName("transactions")]
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];

    [JsonPropertyName("server_knowledge")]
    public long ServerKnowledge { get; init; }
}

public sealed class TransactionResponse
{
    [JsonPropertyName("data")]
    public TransactionData Data { get; init; } = new();
}

public sealed class TransactionData
{
    [JsonPropertyName("transaction")]
    public Transaction Transaction { get; init; } = new();
}

public sealed class SaveTransactionsResponse
{
    [JsonPropertyName("data")]
    public SaveTransactionsData Data { get; init; } = new();
}

public sealed class SaveTransactionsData
{
    [JsonPropertyName("transaction_ids")]
    public IReadOnlyList<string> TransactionIds { get; init; } = [];

    [JsonPropertyName("transaction")]
    public Transaction? Transaction { get; init; }

    [JsonPropertyName("transactions")]
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];

    [JsonPropertyName("duplicate_import_ids")]
    public IReadOnlyList<string> DuplicateImportIds { get; init; } = [];
}

public sealed class Transaction
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public DateOnly Date { get; init; }

    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("account_id")]
    public Guid AccountId { get; init; }

    [JsonPropertyName("account_name")]
    public string AccountName { get; init; } = string.Empty;

    [JsonPropertyName("payee_id")]
    public Guid? PayeeId { get; init; }

    [JsonPropertyName("payee_name")]
    public string? PayeeName { get; init; }

    [JsonPropertyName("transfer_account_id")]
    public Guid? TransferAccountId { get; init; }

    [JsonPropertyName("transfer_transaction_id")]
    public string? TransferTransactionId { get; init; }

    [JsonPropertyName("category_id")]
    public Guid? CategoryId { get; init; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; init; }

    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    [JsonPropertyName("cleared")]
    public string Cleared { get; init; } = string.Empty;

    [JsonPropertyName("approved")]
    public bool Approved { get; init; }

    [JsonPropertyName("flag_color")]
    public string? FlagColor { get; init; }

    [JsonPropertyName("import_id")]
    public string? ImportId { get; init; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }
}

public sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public ErrorDetail Error { get; init; } = new();
}

public sealed class ErrorDetail
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}
