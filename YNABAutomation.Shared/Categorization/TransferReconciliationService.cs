using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public sealed class TransferReconciliationService(
    YnabDbContext db,
    IYnabApiClient ynab,
    IOptions<TransferOptions> options)
{
    private readonly TransferOptions _options = options.Value;

    public async Task<bool> ProcessAsync(
        Transaction transaction,
        IReadOnlyList<Transaction> allTransactions,
        ProcessedYnabTransaction local,
        ProcessingRun run,
        IReadOnlyDictionary<Guid, Account> accounts,
        CancellationToken cancellationToken)
    {
        var existing = await db.TransferCandidates
            .SingleOrDefaultAsync(item => item.YnabTransactionId == transaction.Id, cancellationToken);
        if (existing?.Status is TransferCandidateStatus.Repaired or TransferCandidateStatus.Matched)
        {
            local.IsTransfer = true;
            run.SkippedCount++;
            return true;
        }

        var transferMetadata = transaction.TransferAccountId is not null
            || transaction.TransferTransactionId is not null;
        var matches = TransferMatcher.FindMatches(transaction, allTransactions, _options.MatchingDateWindowDays);
        var transferLooking = transferMetadata
            || matches.Count > 0
            || allTransactions.Any(item => item.AccountId != transaction.AccountId
                && string.Equals(item.AccountName, transaction.PayeeName, StringComparison.OrdinalIgnoreCase))
            || accounts.Values.Any(account => account.Id != transaction.AccountId
                && (string.Equals(account.Name, transaction.PayeeName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        $"Transfer : {account.Name}",
                        transaction.PayeeName,
                        StringComparison.OrdinalIgnoreCase)));
        if (!transferLooking)
        {
            return false;
        }

        var candidate = existing ?? new TransferCandidate
        {
            Id = Guid.NewGuid(),
            YnabTransactionId = transaction.Id,
            FirstSeenAt = DateTimeOffset.UtcNow
        };
        if (existing is null)
        {
            db.TransferCandidates.Add(candidate);
        }

        candidate.LastSeenAt = DateTimeOffset.UtcNow;
        candidate.AccountId = transaction.AccountId;
        candidate.AccountName = transaction.AccountName;
        candidate.TransactionDate = transaction.Date;
        candidate.Amount = transaction.Amount;
        candidate.PayeeName = transaction.PayeeName;
        candidate.Cleared = TransferMatcher.IsCleared(transaction);
        candidate.ExistingYnabTransfer = transferMetadata;
        candidate.PlausibleMatchesJson = JsonSerializer.Serialize(matches.Select(item => new
        {
            item.Transaction.Id,
            item.Transaction.Date,
            item.Transaction.Amount,
            item.Transaction.AccountId,
            item.AccountName,
            item.Transaction.PayeeName,
            item.Transaction.Cleared,
            item.Transaction.TransferAccountId,
            item.Transaction.TransferTransactionId
        }));

        if (!candidate.Cleared)
        {
            return SetPending(candidate, "Transfer matching waits until both transactions are cleared.", local, run);
        }

        var clearedMatches = matches.Where(item => TransferMatcher.IsCleared(item.Transaction)).ToArray();
        if (clearedMatches.Length == 0)
        {
            if (transferMetadata)
            {
                return SetReview(candidate, "Existing YNAB transfer metadata has no valid cleared counterpart.", local, run);
            }

            return SetPending(candidate, "No cleared counterpart exists yet; the configured matching window remains open.", local, run);
        }

        if (clearedMatches.Length != 1)
        {
            return SetReview(candidate, $"Automatic transfer matching rejected {clearedMatches.Length} plausible counterparts.", local, run);
        }

        var counterpart = clearedMatches[0].Transaction;
        var counterpartAccount = accounts.GetValueOrDefault(counterpart.AccountId);
        var sourceAccount = accounts.GetValueOrDefault(transaction.AccountId);
        if (sourceAccount?.TransferPayeeId is not Guid sourcePayeeId
            || counterpartAccount?.TransferPayeeId is not Guid counterpartPayeeId)
        {
            return SetReview(candidate, "YNAB did not provide transfer payees for both accounts.", local, run);
        }

        if (transaction.TransferTransactionId == counterpart.Id
            && counterpart.TransferTransactionId == transaction.Id
            && transaction.TransferAccountId == counterpart.AccountId
            && counterpart.TransferAccountId == transaction.AccountId)
        {
            candidate.Status = TransferCandidateStatus.Matched;
            candidate.MatchedTransactionId = counterpart.Id;
            local.IsTransfer = true;
            run.SkippedCount++;
            return true;
        }

        var current = await ynab.GetTransactionAsync(transaction.Id, cancellationToken);
        var currentCounterpart = await ynab.GetTransactionAsync(counterpart.Id, cancellationToken);
        if (current.Data.Transaction.Deleted || currentCounterpart.Data.Transaction.Deleted)
        {
            return SetReview(candidate, "A transfer participant changed or was deleted while being evaluated.", local, run);
        }
        if (current.Data.Transaction.AccountId != transaction.AccountId
            || current.Data.Transaction.Amount != transaction.Amount
            || !TransferMatcher.IsCleared(current.Data.Transaction)
            || currentCounterpart.Data.Transaction.AccountId != counterpart.AccountId
            || currentCounterpart.Data.Transaction.Amount != counterpart.Amount
            || !TransferMatcher.IsCleared(currentCounterpart.Data.Transaction))
        {
            return SetReview(candidate, "A transfer participant changed while being evaluated.", local, run);
        }

        await ynab.UpdateTransactionAsync(
            new UpdateTransactionCategoryRequest(transaction.Id, null, null, counterpartAccount.TransferPayeeId),
            cancellationToken);
        await ynab.UpdateTransactionAsync(
            new UpdateTransactionCategoryRequest(counterpart.Id, null, null, sourceAccount.TransferPayeeId),
            cancellationToken);

        candidate.Status = TransferCandidateStatus.Repaired;
        candidate.MatchedTransactionId = counterpart.Id;
        candidate.Reason = "Unique cleared counterpart repaired using YNAB account transfer payees.";
        local.IsTransfer = true;
        local.Status = TransactionProcessingStatus.Applied;
        local.CategorizedAt = DateTimeOffset.UtcNow;
        run.AppliedCount++;
        return true;
    }

    private static bool SetPending(
        TransferCandidate candidate,
        string reason,
        ProcessedYnabTransaction local,
        ProcessingRun run)
    {
        candidate.Status = TransferCandidateStatus.PendingMatch;
        candidate.Reason = reason;
        local.IsTransfer = true;
        run.SkippedCount++;
        return true;
    }

    private bool SetReview(
        TransferCandidate candidate,
        string reason,
        ProcessedYnabTransaction local,
        ProcessingRun run)
    {
        candidate.Status = TransferCandidateStatus.ReviewRequired;
        candidate.Reason = reason;
        local.IsTransfer = true;
        local.Status = TransactionProcessingStatus.ReviewRequired;
        if (!local.Decisions.Any(decision =>
            decision.Status == CategorizationDecisionStatus.ReviewRequired
            && decision.Reason == $"Transfer review: {reason}"))
        {
            var decision = new CategorizationDecision
            {
                Id = Guid.NewGuid(),
                ProcessingRunId = run.Id,
                ProcessedYnabTransactionId = local.Id,
                Direction = local.Direction,
                RuleSource = RuleSource.None,
                Status = CategorizationDecisionStatus.ReviewRequired,
                Reason = $"Transfer review: {reason}",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.CategorizationDecisions.Add(decision);
            local.Decisions.Add(decision);
        }
        run.ReviewCount++;
        return true;
    }
}
