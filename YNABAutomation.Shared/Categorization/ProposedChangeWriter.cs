using Microsoft.Extensions.Logging;

namespace YNABAutomationConsole.Categorization;

public interface IProposedChangeWriter
{
    void Write(string transactionId, string? payeeName, long amount, Guid categoryId, decimal? aiConfidence, string reason);
}

public sealed class ConsoleProposedChangeWriter : IProposedChangeWriter
{
    private readonly ILogger<ConsoleProposedChangeWriter> _logger;

    public ConsoleProposedChangeWriter(ILogger<ConsoleProposedChangeWriter> logger)
    {
        _logger = logger;
    }

    public void Write(string transactionId, string? payeeName, long amount, Guid categoryId, decimal? aiConfidence, string reason)
    {
        _logger.LogInformation(
            "DRY RUN: transaction={TransactionId}, payee='{PayeeName}', amount={Amount}, " +
            "proposed_category={CategoryId}, ai_confidence={AiConfidence}, reason='{Reason}'. No YNAB change was made.",
            transactionId,
            payeeName,
            amount,
            categoryId,
            aiConfidence?.ToString("0.####") ?? "n/a",
            reason);
    }
}
