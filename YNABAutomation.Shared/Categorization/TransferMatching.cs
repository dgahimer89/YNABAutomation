using System.Text.Json;
using Microsoft.Extensions.Options;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public sealed class TransferOptions
{
    public const string SectionName = "Transfers";
    public int MatchingDateWindowDays { get; set; } = 3;
}

public sealed record TransferMatchCandidate(Transaction Transaction, string AccountName);

public static class TransferMatcher
{
    public static IReadOnlyList<TransferMatchCandidate> FindMatches(
        Transaction transaction,
        IEnumerable<Transaction> transactions,
        int dateWindowDays)
    {
        return transactions
            .Where(other => other.Id != transaction.Id
                && other.AccountId != transaction.AccountId
                && other.Amount == -transaction.Amount
                && Math.Abs(other.Date.DayNumber - transaction.Date.DayNumber) <= dateWindowDays
                && IsCleared(other))
            .Select(other => new TransferMatchCandidate(other, other.AccountName))
            .ToArray();
    }

    public static bool IsCleared(Transaction transaction) =>
        string.Equals(transaction.Cleared, "cleared", StringComparison.OrdinalIgnoreCase);
}
