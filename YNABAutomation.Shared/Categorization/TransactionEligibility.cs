using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public sealed record TransactionEligibility(bool IsEligible, bool IsInflow, bool IsTransfer, string Reason);

public static class TransactionClassifier
{
    public static TransactionEligibility Classify(Transaction transaction)
    {
        if (transaction.Deleted)
        {
            return new(false, transaction.Amount > 0, false, "Deleted transaction.");
        }

        if (transaction.TransferAccountId is not null || transaction.TransferTransactionId is not null)
        {
            return new(false, transaction.Amount > 0, true, "Transfer transactions do not receive normal budget categories.");
        }

        if (string.IsNullOrWhiteSpace(transaction.PayeeName))
        {
            return new(false, transaction.Amount > 0, false, "Transaction has no payee.");
        }

        return new(true, transaction.Amount > 0, false, "Eligible transaction.");
    }
}
