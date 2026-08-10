namespace YNABAutomationConsole.Categorization;

public interface IProposedChangeWriter
{
    void Write(string transactionId, string? payeeName, long amount, Guid categoryId, string reason);
}

public sealed class ConsoleProposedChangeWriter : IProposedChangeWriter
{
    public void Write(string transactionId, string? payeeName, long amount, Guid categoryId, string reason)
    {
        Console.WriteLine(
            $"DRY RUN: transaction={transactionId}, payee='{payeeName}', amount={amount}, " +
            $"proposed_category={categoryId}, reason='{reason}'. No YNAB change was made.");
    }
}
