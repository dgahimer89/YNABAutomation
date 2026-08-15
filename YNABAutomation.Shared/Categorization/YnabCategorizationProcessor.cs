using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.ClientModel;
using System.Text.Json;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public sealed record CategorizationRunResult(
    int Fetched,
    int Applied,
    int Proposed,
    int ReviewRequired,
    int Skipped,
    int Failed,
    IReadOnlyList<string> AiFailureMessages);

public sealed class YnabCategorizationProcessor(
    YnabDbContext db,
    IYnabApiClient ynab,
    PayeeNormalizer normalizer,
    CategoryCandidateSelector selector,
    AutoApplyPolicy policy,
    IOptions<CategorizationOptions> options,
    IOptions<OpenAiOptions> openAiOptions,
    IAiCategorizer aiCategorizer,
    IProposedChangeWriter proposedChangeWriter,
    TransferReconciliationService? transferReconciliation = null)
{
    private readonly CategorizationOptions _options = options.Value;
    private readonly OpenAiOptions _openAiOptions = openAiOptions.Value;
    private readonly TransferReconciliationService _transferReconciliation =
        transferReconciliation ?? new TransferReconciliationService(
            db, ynab, Options.Create(new TransferOptions()));

    public async Task<CategorizationRunResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        await RecoverPendingUpdatesAsync(cancellationToken);

        var run = new ProcessingRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow
        };
        db.ProcessingRuns.Add(run);
        var aiFailureMessages = new List<string>();

        var unapprovedTransactions = (await ynab.GetTransactionsAsync(
            new GetTransactionsOptions { Type = TransactionType.Unapproved }, cancellationToken))
            .Data.Transactions;
        await ApproveEligibleTransactionsAsync(unapprovedTransactions, cancellationToken);
        var allTransactions = (await ynab.GetTransactionsAsync(cancellationToken: cancellationToken))
            .Data.Transactions;
        var response = await ynab.GetTransactionsAsync(
            new GetTransactionsOptions { Type = TransactionType.Uncategorized }, cancellationToken);
        run.FetchedCount = response.Data.Transactions.Count;
        var accounts = (await ynab.GetPlansAsync(
            new GetPlansOptions { IncludeAccounts = true }, cancellationToken))
            .Data.Plans.SelectMany(plan => plan.Accounts).ToDictionary(account => account.Id);

        foreach (var transaction in response.Data.Transactions)
        {
            if (IsReconciliationAdjustment(transaction))
            {
                continue;
            }

            await ProcessTransactionAsync(
                run, transaction, allTransactions, accounts, aiFailureMessages, cancellationToken);
        }

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new(
            run.FetchedCount,
            run.AppliedCount,
            run.ProposedCount,
            run.ReviewCount,
            run.SkippedCount,
            run.FailedCount,
            aiFailureMessages);
    }

    private async Task ProcessTransactionAsync(
        ProcessingRun run,
        Transaction transaction,
        IReadOnlyList<Transaction> allTransactions,
        IReadOnlyDictionary<Guid, Account> accounts,
        ICollection<string> aiFailureMessages,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var local = await db.ProcessedYnabTransactions
            .Include(item => item.Decisions)
            .Include(item => item.AiDecisions)
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

        if (await _transferReconciliation.ProcessAsync(
            transaction, allTransactions, local, run, accounts, cancellationToken))
        {
            return;
        }

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
            if (await TryProcessAiSuggestionAsync(run, transaction, local, cancellationToken))
            {
                return;
            }

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

        async Task<bool> TryProcessAiSuggestionAsync(
            ProcessingRun run,
            Transaction transaction,
            ProcessedYnabTransaction local,
            CancellationToken cancellationToken)
        {
            if (!aiCategorizer.IsConfigured)
            {
                return false;
            }

            if (local.AiDecisions.Any(decision => decision.Outcome == AiDecisionOutcome.Suggested))
            {
                local.Status = TransactionProcessingStatus.ReviewRequired;
                run.ReviewCount++;
                return true;
            }

            IReadOnlyList<CategoryGroup> categoryGroups;
            try
            {
                categoryGroups = (await ynab.GetCategoriesAsync(cancellationToken)).Data.CategoryGroups
                    .Where(group => !group.Hidden && !group.Deleted)
                    .Select(group => new CategoryGroup
                    {
                        Id = group.Id,
                        Name = group.Name,
                        Categories = group.Categories.Where(category => !category.Hidden && !category.Deleted).ToArray()
                    })
                    .Where(group => group.Categories.Count > 0)
                    .ToArray();
                var categories = categoryGroups
                    .SelectMany(group => group.Categories.Select(category => new AiCategory(category.Id, category.Name, group.Name)))
                    .ToArray();
                var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
                var history = await db.CategorizationDecisions
                    .AsNoTracking()
                    .Where(decision => decision.NormalizedPayee == local.NormalizedPayee
                        && decision.Direction == local.Direction
                        && decision.IsManualObservation
                        && decision.Status == CategorizationDecisionStatus.ManualApplied
                        && decision.SelectedCategoryId != null)
                    .GroupBy(decision => decision.SelectedCategoryId)
                    .Select(group => new { CategoryId = group.Key!.Value, Count = group.Count() })
                    .OrderByDescending(item => item.Count)
                    .Take(_openAiOptions.MaximumHistoricalObservations)
                    .ToListAsync(cancellationToken);
                var result = await aiCategorizer.CategorizeAsync(new AiCategorizationRequest(
                    transaction.PayeeName,
                    local.NormalizedPayee!,
                    transaction.Date,
                    transaction.Amount,
                    local.Direction,
                    transaction.AccountName,
                    transaction.Memo,
                    history.Where(item => categoryNames.ContainsKey(item.CategoryId))
                        .Select(item => new AiHistoricalObservation(item.CategoryId, categoryNames[item.CategoryId], item.Count))
                        .ToArray(),
                    categories), cancellationToken);
                return await RecordAiResultAsync(run, transaction, local, result, categoryNames, cancellationToken);
            }
            catch (Exception exception) when (exception is ClientResultException or HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
            {
                local.Status = TransactionProcessingStatus.ReviewRequired;
                AddAiDecision(local, null, null, null, false, false, AiDecisionOutcome.Failed, exception.Message);
                aiFailureMessages.Add(exception.Message);
                AddDecision(run, local, null, CategorizationDecisionStatus.Failed, 0, null, "OpenAI categorization failed.");
                run.FailedCount++;
                return true;
            }
        }

        async Task<bool> RecordAiResultAsync(
            ProcessingRun run,
            Transaction transaction,
            ProcessedYnabTransaction local,
            AiCategorizationResult result,
            IReadOnlyDictionary<Guid, string> categoryNames,
            CancellationToken cancellationToken)
        {
            var validCategory = Guid.TryParse(result.CategoryId, out var categoryId) && categoryNames.ContainsKey(categoryId);
            var validAlternative = string.IsNullOrWhiteSpace(result.AlternativeCategoryId)
                || (Guid.TryParse(result.AlternativeCategoryId, out var alternativeId) && categoryNames.ContainsKey(alternativeId));
            if (result.Confidence is < 0 or > 1
                || !validAlternative
                || (!string.IsNullOrWhiteSpace(result.CategoryId) && !validCategory)
                || (!result.RequiresReview && !validCategory))
            {
                local.Status = TransactionProcessingStatus.ReviewRequired;
                AddAiDecision(local, validCategory ? categoryId : null, null, result, false, false,
                    AiDecisionOutcome.RejectedInvalidOutput, "OpenAI returned an invalid or unknown category.");
                AddDecision(run, local, null, CategorizationDecisionStatus.Failed, 0, null, "OpenAI returned invalid structured output.");
                run.ReviewCount++;
                return true;
            }

            var thresholdMet = validCategory && !result.RequiresReview
                && result.Confidence >= _openAiOptions.AutoApplyConfidenceThreshold;
            var aiDecision = AddAiDecision(local, validCategory ? categoryId : null,
                validAlternative ? ParseGuidOrNull(result.AlternativeCategoryId) : null, result, thresholdMet, false,
                AiDecisionOutcome.Suggested, null);
            aiDecision.ProposedCategoryName = validCategory ? categoryNames[categoryId] : null;
            aiDecision.AlternativeCategoryName = aiDecision.AlternativeCategoryId is Guid alternativeCategoryId
                ? categoryNames[alternativeCategoryId]
                : null;
            var aiCandidate = validCategory
                ? new CategoryCandidate(categoryId, RuleSource.Ai, 0, result.Confidence, result.RequiresReview, result.Reason)
                : null;

            if (!thresholdMet || aiCandidate is null)
            {
                local.Status = TransactionProcessingStatus.ReviewRequired;
                AddDecision(run, local, aiCandidate, CategorizationDecisionStatus.ReviewRequired, 0, result.Confidence,
                    result.RequiresReview ? "OpenAI requested manual review." : "OpenAI confidence is below the configured auto-apply threshold.");
                run.ReviewCount++;
                return true;
            }

            if (_options.DryRun)
            {
                local.Status = TransactionProcessingStatus.DryRun;
                AddDecision(run, local, aiCandidate, CategorizationDecisionStatus.DryRun, 0, result.Confidence, result.Reason);
                proposedChangeWriter.Write(transaction.Id, transaction.PayeeName, transaction.Amount, categoryId, result.Confidence, result.Reason);
                run.ProposedCount++;
                return true;
            }

            var pending = new PendingCategoryUpdate
            {
                Id = Guid.NewGuid(),
                ProcessedYnabTransactionId = local.Id,
                CategoryId = categoryId,
                Status = PendingUpdateStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                RequestId = Guid.NewGuid()
            };
            db.PendingCategoryUpdates.Add(pending);
            local.Status = TransactionProcessingStatus.UpdatePending;
            AddDecision(run, local, aiCandidate, CategorizationDecisionStatus.Pending, 0, result.Confidence, result.Reason);
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                if (local.Status == TransactionProcessingStatus.Applied)
                {
                    return true;
                }

                var current = await ynab.GetTransactionAsync(transaction.Id, cancellationToken);
                if (current.Data.Transaction.CategoryId is Guid currentCategoryId)
                {
                    pending.Status = currentCategoryId == categoryId ? PendingUpdateStatus.Succeeded : PendingUpdateStatus.Failed;
                    pending.CompletedAt = DateTimeOffset.UtcNow;
                    pending.LastError = currentCategoryId == categoryId ? null : "Transaction was categorized externally.";
                    local.Status = TransactionProcessingStatus.Applied;
                    local.CategorizedAt = pending.CompletedAt;
                    aiDecision.WasAutoApplied = currentCategoryId == categoryId;
                    aiDecision.Outcome = currentCategoryId == categoryId ? AiDecisionOutcome.AutoApplied : AiDecisionOutcome.Failed;
                    aiDecision.FailureReason = pending.LastError;
                    local.Decisions.Last().Status = currentCategoryId == categoryId
                        ? CategorizationDecisionStatus.AutoApplied
                        : CategorizationDecisionStatus.Skipped;
                    run.AppliedCount++;
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                }

                await ynab.UpdateTransactionAsync(
                    new UpdateTransactionCategoryRequest(
                        transaction.Id, categoryId, IsCleared(transaction)), cancellationToken);
                pending.Status = PendingUpdateStatus.Succeeded;
                pending.CompletedAt = DateTimeOffset.UtcNow;
                local.Status = TransactionProcessingStatus.Applied;
                local.CategorizedAt = pending.CompletedAt;
                local.Decisions.Last().Status = CategorizationDecisionStatus.AutoApplied;
                aiDecision.WasAutoApplied = true;
                aiDecision.Outcome = AiDecisionOutcome.AutoApplied;
                aiDecision.ResolvedAt = pending.CompletedAt;
                run.AppliedCount++;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is YnabApiException or HttpRequestException or TaskCanceledException)
            {
                pending.Status = PendingUpdateStatus.Failed;
                pending.Attempts++;
                pending.LastAttemptAt = DateTimeOffset.UtcNow;
                pending.LastError = exception.Message;
                local.Status = TransactionProcessingStatus.ReviewRequired;
                local.Decisions.Last().Status = CategorizationDecisionStatus.Failed;
                aiDecision.Outcome = AiDecisionOutcome.Failed;
                aiDecision.FailureReason = exception.Message;
                run.FailedCount++;
                await db.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        AiCategorizationDecision AddAiDecision(
            ProcessedYnabTransaction transaction,
            Guid? proposedCategoryId,
            Guid? alternativeCategoryId,
            AiCategorizationResult? result,
            bool thresholdMet,
            bool autoApplied,
            AiDecisionOutcome outcome,
            string? failureReason)
        {
            var decision = new AiCategorizationDecision
            {
                Id = Guid.NewGuid(),
                ProcessedYnabTransactionId = transaction.Id,
                ProposedCategoryId = proposedCategoryId,
                AlternativeCategoryId = alternativeCategoryId,
                Confidence = result?.Confidence,
                Reason = result?.Reason ?? failureReason ?? "OpenAI categorization failed.",
                Model = _openAiOptions.Model,
                RequiresReview = result?.RequiresReview ?? true,
                MetAutoApplyThreshold = thresholdMet,
                WasAutoApplied = autoApplied,
                Outcome = outcome,
                FailureReason = failureReason,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.AiCategorizationDecisions.Add(decision);
            transaction.AiDecisions.Add(decision);
            return decision;
        }

        static Guid? ParseGuidOrNull(string? value) =>
            Guid.TryParse(value, out var parsed) ? parsed : null;

        if (_options.DryRun)
        {
            local.Status = TransactionProcessingStatus.DryRun;
            AddDecision(run, local, candidate, CategorizationDecisionStatus.DryRun, candidate.SampleSize, candidate.Consistency, reason);
            proposedChangeWriter.Write(transaction.Id, transaction.PayeeName, transaction.Amount, candidate.CategoryId!.Value, null, reason);
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
            var current = await ynab.GetTransactionAsync(transaction.Id, cancellationToken);
            if (current.Data.Transaction.CategoryId is Guid currentCategoryId)
            {
                pending.Status = currentCategoryId == candidate.CategoryId
                    ? PendingUpdateStatus.Succeeded
                    : PendingUpdateStatus.Failed;
                pending.CompletedAt = DateTimeOffset.UtcNow;
                pending.LastError = currentCategoryId == candidate.CategoryId
                    ? null
                    : "Transaction was categorized externally.";
                local.Status = TransactionProcessingStatus.Applied;
                local.CategorizedAt = pending.CompletedAt;
                local.Decisions.Last().Status = currentCategoryId == candidate.CategoryId
                    ? CategorizationDecisionStatus.AutoApplied
                    : CategorizationDecisionStatus.Skipped;
                run.AppliedCount++;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            await ynab.UpdateTransactionAsync(
                new UpdateTransactionCategoryRequest(
                    transaction.Id, candidate.CategoryId, IsCleared(transaction)), cancellationToken);
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

    private async Task ApproveEligibleTransactionsAsync(
        IReadOnlyList<Transaction> transactions,
        CancellationToken cancellationToken)
    {
        var approvals = transactions
            .Where(transaction => !transaction.Approved
                && transaction.CategoryId is not null
                && IsCleared(transaction)
                && !IsReconciliationAdjustment(transaction))
            .Select(transaction => UpdateTransactionsRequest.ById(
                transaction.Id, null, approved: true))
            .ToArray();
        if (approvals.Length > 0)
        {
            await ynab.UpdateTransactionsAsync(approvals, cancellationToken);
        }
    }

    private static bool IsCleared(Transaction transaction) =>
        TransferMatcher.IsCleared(transaction);

    private static bool IsReconciliationAdjustment(Transaction transaction) =>
        string.Equals(
            transaction.PayeeName,
            "Reconciliation Balance Adjustment",
            StringComparison.OrdinalIgnoreCase);

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
