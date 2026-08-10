namespace YNABAutomationConsole.Categorization;

public enum TransactionProcessingStatus
{
    Seen,
    ReviewRequired,
    UpdatePending,
    Applied,
    Skipped,
    Failed,
    DryRun
}

public enum TransactionDirection
{
    Outflow,
    Inflow
}

public enum CategorizationDecisionStatus
{
    Pending,
    ReviewRequired,
    AutoApplied,
    Skipped,
    Failed,
    DryRun,
    ManualApplied
}

public enum RuleSource
{
    None,
    Explicit,
    Learned
}

public enum PendingUpdateStatus
{
    Pending,
    Succeeded,
    Failed
}

public sealed class ProcessingRun
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int FetchedCount { get; set; }
    public int AppliedCount { get; set; }
    public int ProposedCount { get; set; }
    public int ReviewCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
}

public sealed class ProcessedYnabTransaction
{
    public Guid Id { get; set; }
    public string YnabTransactionId { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public long Amount { get; set; }
    public string? PayeeName { get; set; }
    public string? NormalizedPayee { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? Memo { get; set; }
    public string? AccountName { get; set; }
    public bool IsInflow { get; set; }
    public bool IsTransfer { get; set; }
    public TransactionProcessingStatus Status { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? CategorizedAt { get; set; }
    public ICollection<CategorizationDecision> Decisions { get; set; } = [];
    public ICollection<PendingCategoryUpdate> PendingUpdates { get; set; } = [];
}

public sealed class CategorizationDecision
{
    public Guid Id { get; set; }
    public Guid ProcessingRunId { get; set; }
    public ProcessingRun? ProcessingRun { get; set; }
    public Guid ProcessedYnabTransactionId { get; set; }
    public ProcessedYnabTransaction? ProcessedYnabTransaction { get; set; }
    public string? NormalizedPayee { get; set; }
    public TransactionDirection Direction { get; set; }
    public Guid? SelectedCategoryId { get; set; }
    public RuleSource RuleSource { get; set; }
    public bool IsManualObservation { get; set; }
    public CategorizationDecisionStatus Status { get; set; }
    public decimal? Consistency { get; set; }
    public int SampleSize { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MerchantRule
{
    public Guid Id { get; set; }
    public string NormalizedPayee { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public TransactionDirection Direction { get; set; }
    public bool IsExplicit { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PendingCategoryUpdate
{
    public Guid Id { get; set; }
    public Guid ProcessedYnabTransactionId { get; set; }
    public ProcessedYnabTransaction? ProcessedYnabTransaction { get; set; }
    public Guid CategoryId { get; set; }
    public PendingUpdateStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public Guid RequestId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
