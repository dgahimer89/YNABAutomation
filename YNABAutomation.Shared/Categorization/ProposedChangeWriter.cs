namespace YNABAutomationConsole.Categorization;

public interface IProposedChangeWriter
{
    void Write(string transactionId, string? payeeName, long amount, Guid categoryId, decimal? aiConfidence, string reason);
}

public sealed class ConsoleProposedChangeWriter : IProposedChangeWriter
{
    public void Write(string transactionId, string? payeeName, long amount, Guid categoryId, decimal? aiConfidence, string reason)
    {
        Console.WriteLine(
            $"DRY RUN: transaction={transactionId}, payee='{payeeName}', amount={amount}, " +
            $"proposed_category={categoryId}, ai_confidence={aiConfidence?.ToString("0.####") ?? "n/a"}, " +
            $"reason='{reason}'. No YNAB change was made.");
    }
}
