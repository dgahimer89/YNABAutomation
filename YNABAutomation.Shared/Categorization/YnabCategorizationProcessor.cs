using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public sealed record CategorizationRunResult(int Fetched, int Applied, int Proposed, int ReviewRequired, int Skipped, int Failed);

public sealed class YnabCategorizationProcessor(
    YnabDbContext db,
    IYnabApiClient ynab,
    PayeeNormalizer normalizer,
    CategoryCandidateSelector selector,
    AutoApplyPolicy policy,
    IOptions<CategorizationOptions> options,
    IProposedChangeWriter proposedChangeWriter)
{
    private readonly CategorizationOptions _options = options.Value;

    public async Task<CategorizationRunResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        await RecoverPendingUpdatesAsync(cancellationToken);

        var run = new ProcessingRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow
        };
        db.ProcessingRuns.Add(run);

        var response = await ynab.GetTransactionsAsync(
            new GetTransactionsOptions { Type = TransactionType.Uncategorized }, cancellationToken);
        run.FetchedCount = response.Data.Transactions.Count;

        foreach (var transaction in response.Data.Transactions)
        {
            await ProcessTransactionAsync(run, transaction, cancellationToken);
        }

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new(run.FetchedCount, run.AppliedCount, run.ProposedCount, run.ReviewCount, run.SkippedCount, run.FailedCount);
    }

    private async Task ProcessTransactionAsync(
        ProcessingRun run,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var local = await db.ProcessedYnabTransactions
            .Include(item => item.Decisions)
            .SingleOrDefaultAsync(item => item.YnabTransactionId == transaction.Id, cancellationToken);
        if (local is null)
        {
            local = new ProcessedYnabTransaction
            {
                Id = Guid.NewGuid(),
                YnabTransactionId = transaction.Id,
                FirstSeenAt = now
            };
            db.ProcessedYnabTransactions.Add(local);
        }

        local.LastSeenAt = now;
        local.TransactionDate = transaction.Date;
        local.Amount = transaction.Amount;
        local.PayeeName = transaction.PayeeName;
        local.NormalizedPayee = normalizer.Normalize(transaction.PayeeName);
        var eligibility = TransactionClassifier.Classify(transaction);
        local.IsInflow = eligibility.IsInflow;
        local.Direction = eligibility.IsInflow ? TransactionDirection.Inflow : TransactionDirection.Outflow;
        local.IsTransfer = eligibility.IsTransfer;
        local.Memo = transaction.Memo;
        local.AccountName = transaction.AccountName;

        if (local.Status == TransactionProcessingStatus.Applied)
        {
            run.SkippedCount++;
            return;
        }

        if (!eligibility.IsEligible || local.NormalizedPayee is null)
        {
            local.Status = TransactionProcessingStatus.Skipped;
            AddDecision(run, local, null, CategorizationDecisionStatus.Skipped, 0, null, eligibility.Reason);
            run.SkippedCount++;
            return;
        }

        var candidate = await selector.SelectAsync(local.NormalizedPayee, local.Direction, cancellationToken);
        if (!policy.CanAutoApply(candidate, out var reason))
        {
            local.Status = TransactionProcessingStatus.ReviewRequired;
            if (!local.Decisions.Any(decision =>
                decision.Status == CategorizationDecisionStatus.ReviewRequired &&
                decision.SelectedCategoryId == candidate.CategoryId &&
                decision.Reason == reason))
            {
                AddDecision(run, local, candidate, CategorizationDecisionStatus.ReviewRequired, candidate.SampleSize, candidate.Consistency, reason);
            }
            run.ReviewCount++;
            return;
        }

        if (_options.DryRun)
        {
            local.Status = TransactionProcessingStatus.DryRun;
            AddDecision(run, local, candidate, CategorizationDecisionStatus.DryRun, candidate.SampleSize, candidate.Consistency, reason);
            proposedChangeWriter.Write(transaction.Id, transaction.PayeeName, transaction.Amount, candidate.CategoryId!.Value, reason);
            run.ProposedCount++;
            return;
        }

        var pending = new PendingCategoryUpdate
        {
            Id = Guid.NewGuid(),
            ProcessedYnabTransactionId = local.Id,
            CategoryId = candidate.CategoryId!.Value,
            Status = PendingUpdateStatus.Pending,
            CreatedAt = now,
            RequestId = Guid.NewGuid()
        };
        db.PendingCategoryUpdates.Add(pending);
        local.Status = TransactionProcessingStatus.UpdatePending;
        AddDecision(run, local, candidate, CategorizationDecisionStatus.Pending, candidate.SampleSize, candidate.Consistency, reason);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await ynab.UpdateTransactionAsync(
                new UpdateTransactionCategoryRequest(transaction.Id, candidate.CategoryId), cancellationToken);
            pending.Status = PendingUpdateStatus.Succeeded;
            pending.CompletedAt = DateTimeOffset.UtcNow;
            local.Status = TransactionProcessingStatus.Applied;
            local.CategorizedAt = pending.CompletedAt;
            local.Decisions.Last().Status = CategorizationDecisionStatus.AutoApplied;
            run.AppliedCount++;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is YnabApiException or HttpRequestException or TaskCanceledException)
        {
            pending.Status = PendingUpdateStatus.Failed;
            pending.Attempts++;
            pending.LastAttemptAt = DateTimeOffset.UtcNow;
            pending.LastError = exception.Message;
            local.Status = TransactionProcessingStatus.Failed;
            local.Decisions.Last().Status = CategorizationDecisionStatus.Failed;
            run.FailedCount++;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RecoverPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        var pending = await db.PendingCategoryUpdates
            .Include(update => update.ProcessedYnabTransaction)
            .ThenInclude(transaction => transaction!.Decisions)
            .Where(update => update.Status != PendingUpdateStatus.Succeeded)
            .ToListAsync(cancellationToken);

        foreach (var update in pending)
        {
            if (update.ProcessedYnabTransaction is null)
            {
                continue;
            }

            try
            {
                var current = await ynab.GetTransactionAsync(
                    update.ProcessedYnabTransaction.YnabTransactionId,
                    cancellationToken);
                if (current.Data.Transaction.CategoryId is Guid currentCategoryId)
                {
                    update.Status = PendingUpdateStatus.Succeeded;
                    update.CompletedAt = DateTimeOffset.UtcNow;
                    update.ProcessedYnabTransaction.Status = TransactionProcessingStatus.Applied;
                    update.ProcessedYnabTransaction.CategorizedAt = update.CompletedAt;
                    update.LastError = currentCategoryId == update.CategoryId
                        ? null
                        : "Transaction was categorized externally.";
                }
                else
                {
                    await ynab.UpdateTransactionAsync(
                        new UpdateTransactionCategoryRequest(
                            update.ProcessedYnabTransaction.YnabTransactionId, update.CategoryId),
                        cancellationToken);
                    update.Status = PendingUpdateStatus.Succeeded;
                    update.CompletedAt = DateTimeOffset.UtcNow;
                    update.ProcessedYnabTransaction.Status = TransactionProcessingStatus.Applied;
                    update.ProcessedYnabTransaction.CategorizedAt = update.CompletedAt;
                    update.LastError = null;
                }
                update.Status = PendingUpdateStatus.Succeeded;
                var decision = update.ProcessedYnabTransaction.Decisions
                    .OrderByDescending(item => item.CreatedAt)
                    .FirstOrDefault(item => item.SelectedCategoryId == update.CategoryId);
                if (decision is not null)
                {
                    decision.Status = update.LastError is null
                        ? CategorizationDecisionStatus.AutoApplied
                        : CategorizationDecisionStatus.Skipped;
                    if (update.LastError is not null)
                    {
                        decision.Reason = update.LastError;
                    }
                }
                update.Attempts++;
                update.LastAttemptAt = update.CompletedAt;
                update.LastError = null;
            }
            catch (Exception exception) when (exception is YnabApiException or HttpRequestException or TaskCanceledException)
            {
                update.Status = PendingUpdateStatus.Failed;
                update.Attempts++;
                update.LastAttemptAt = DateTimeOffset.UtcNow;
                update.LastError = exception.Message;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddDecision(
        ProcessingRun run,
        ProcessedYnabTransaction transaction,
        CategoryCandidate? candidate,
        CategorizationDecisionStatus status,
        int sampleSize,
        decimal? consistency,
        string reason)
    {
        var decision = new CategorizationDecision
        {
            Id = Guid.NewGuid(),
            ProcessingRunId = run.Id,
            ProcessedYnabTransactionId = transaction.Id,
            NormalizedPayee = transaction.NormalizedPayee,
            Direction = transaction.Direction,
            SelectedCategoryId = candidate?.CategoryId,
            RuleSource = candidate?.Source ?? RuleSource.None,
            Status = status,
            SampleSize = sampleSize,
            Consistency = consistency,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.CategorizationDecisions.Add(decision);
        transaction.Decisions.Add(decision);
    }
}
