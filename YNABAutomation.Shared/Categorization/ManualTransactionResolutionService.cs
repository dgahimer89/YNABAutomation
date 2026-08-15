using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Categorization;

public enum ManualResolutionResult
{
    Applied,
    AlreadyResolved,
    CategorizedExternally,
    NotFound,
    InvalidCategory,
    Failed
}

public sealed record ManualResolutionOutcome(
    ManualResolutionResult Result,
    string Message);

public sealed class ManualTransactionResolutionService(
    YnabDbContext db,
    IYnabApiClient ynab)
{
    public async Task<IReadOnlyList<CategoryGroup>> GetValidCategoryGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await ynab.GetCategoriesAsync(cancellationToken);
        return response.Data.CategoryGroups
            .Where(group => !group.Hidden && !group.Deleted)
            .Select(group => new CategoryGroup
            {
                Id = group.Id,
                Name = group.Name,
                Categories = group.Categories
                    .Where(category => !category.Hidden && !category.Deleted)
                    .ToArray()
            })
            .Where(group => group.Categories.Count > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<Category>> GetValidCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await GetValidCategoryGroupsAsync(cancellationToken);
        return groups
            .SelectMany(group => group.Categories)
            .OrderBy(category => category.Name)
            .ToArray();
    }

    public async Task<ManualResolutionOutcome> ResolveAsync(
        string ynabTransactionId,
        Guid categoryId,
        bool createExplicitRule,
        CancellationToken cancellationToken = default)
    {
        var validCategories = await GetValidCategoriesAsync(cancellationToken);
        var category = validCategories.SingleOrDefault(item => item.Id == categoryId);
        if (category is null)
        {
            return new(ManualResolutionResult.InvalidCategory, "Select a valid current YNAB category.");
        }

        var local = await db.ProcessedYnabTransactions
            .Include(transaction => transaction.Decisions)
            .Include(transaction => transaction.PendingUpdates)
            .Include(transaction => transaction.AiDecisions)
            .SingleOrDefaultAsync(
                transaction => transaction.YnabTransactionId == ynabTransactionId,
                cancellationToken);
        if (local is null)
        {
            return new(ManualResolutionResult.NotFound, "The transaction no longer exists locally.");
        }

        if (local.Status is not (TransactionProcessingStatus.ReviewRequired or TransactionProcessingStatus.UpdatePending))
        {
            return new(ManualResolutionResult.AlreadyResolved,
                "This transaction was already resolved or is no longer awaiting review.");
        }

        var current = await ynab.GetTransactionAsync(ynabTransactionId, cancellationToken);
        if (current.Data.Transaction.CategoryId is Guid externalCategoryId)
        {
            if (externalCategoryId == categoryId)
            {
                var existingPending = local.PendingUpdates
                    .Where(update => update.Status != PendingUpdateStatus.Succeeded)
                    .OrderByDescending(update => update.CreatedAt)
                    .FirstOrDefault();
                if (existingPending is null)
                {
                    existingPending = new PendingCategoryUpdate
                    {
                        Id = Guid.NewGuid(),
                        ProcessedYnabTransactionId = local.Id,
                        CategoryId = categoryId,
                        Status = PendingUpdateStatus.Pending,
                        CreatedAt = DateTimeOffset.UtcNow,
                        RequestId = Guid.NewGuid()
                    };
                    db.PendingCategoryUpdates.Add(existingPending);
                    await db.SaveChangesAsync(cancellationToken);
                }

                await FinalizeLocalResolutionAsync(
                    local, existingPending, categoryId, createExplicitRule,
                    "Manual categorization confirmed from YNAB.", cancellationToken);
                return new(ManualResolutionResult.Applied, "The YNAB categorization was confirmed and saved locally.");
            }

            await ReconcileExternalCategoryAsync(local, externalCategoryId, current.Data.Transaction.CategoryName, cancellationToken);
            return new(ManualResolutionResult.CategorizedExternally,
                $"YNAB already categorizes this transaction as {current.Data.Transaction.CategoryName ?? externalCategoryId.ToString()}.");
        }

        var pending = local.PendingUpdates
            .Where(update => update.Status != PendingUpdateStatus.Succeeded)
            .OrderByDescending(update => update.CreatedAt)
            .FirstOrDefault();
        if (pending is null)
        {
            pending = new PendingCategoryUpdate
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
            await db.SaveChangesAsync(cancellationToken);
            local.Status = TransactionProcessingStatus.ReviewRequired;
        }
        else if (pending.CategoryId != categoryId)
        {
            return new(ManualResolutionResult.Failed,
                "Another resolution is already in progress for this transaction.");
        }

        current = await ynab.GetTransactionAsync(ynabTransactionId, cancellationToken);
        if (current.Data.Transaction.CategoryId is Guid alreadyCategoryId)
        {
            if (alreadyCategoryId == categoryId)
            {
                await FinalizeLocalResolutionAsync(
                    local, pending, categoryId, createExplicitRule,
                    "Manual categorization confirmed from YNAB.", cancellationToken);
                return new(ManualResolutionResult.Applied, "The YNAB categorization was confirmed and saved locally.");
            }

            await ReconcileExternalCategoryAsync(local, alreadyCategoryId, current.Data.Transaction.CategoryName, cancellationToken);
            return new(ManualResolutionResult.CategorizedExternally,
                $"YNAB already categorizes this transaction as {current.Data.Transaction.CategoryName ?? alreadyCategoryId.ToString()}.");
        }

        try
        {
            await ynab.UpdateTransactionAsync(
                new UpdateTransactionCategoryRequest(
                    ynabTransactionId,
                    categoryId,
                    TransferMatcher.IsCleared(current.Data.Transaction)),
                cancellationToken);
        }
        catch (Exception exception) when (exception is YnabApiException or HttpRequestException or TaskCanceledException)
        {
            pending.Status = PendingUpdateStatus.Failed;
            pending.Attempts++;
            pending.LastAttemptAt = DateTimeOffset.UtcNow;
            pending.LastError = exception.Message;
            local.Status = TransactionProcessingStatus.ReviewRequired;
            await db.SaveChangesAsync(cancellationToken);
            return new(ManualResolutionResult.Failed, "YNAB did not accept the categorization. It can be retried safely.");
        }

        await FinalizeLocalResolutionAsync(local, pending, categoryId, createExplicitRule, "Manual categorization.", cancellationToken);
        return new(ManualResolutionResult.Applied, "Transaction categorized successfully.");
    }

    private async Task FinalizeLocalResolutionAsync(
        ProcessedYnabTransaction local,
        PendingCategoryUpdate pending,
        Guid categoryId,
        bool createExplicitRule,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        pending.Status = PendingUpdateStatus.Succeeded;
        pending.CompletedAt = now;
        pending.LastAttemptAt = now;
        pending.Attempts++;
        pending.LastError = null;
        local.Status = TransactionProcessingStatus.Applied;
        local.CategorizedAt = now;
        var aiDecision = local.AiDecisions
            .Where(item => item.Outcome == AiDecisionOutcome.Suggested)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();
        if (aiDecision is not null)
        {
            aiDecision.FinalCategoryId = categoryId;
            aiDecision.Outcome = aiDecision.ProposedCategoryId == categoryId
                ? AiDecisionOutcome.AcceptedByUser
                : AiDecisionOutcome.OverriddenByUser;
            aiDecision.ResolvedAt = now;
        }

        var decision = local.Decisions.FirstOrDefault(item =>
            item.SelectedCategoryId == categoryId &&
            item.IsManualObservation &&
            item.Status == CategorizationDecisionStatus.ManualApplied);
        if (decision is null)
        {
            var run = new ProcessingRun
            {
                Id = Guid.NewGuid(),
                StartedAt = now,
                CompletedAt = now,
                FetchedCount = 1,
                AppliedCount = 1
            };
            db.ProcessingRuns.Add(run);
            var manualDecision = new CategorizationDecision
            {
                Id = Guid.NewGuid(),
                ProcessingRun = run,
                ProcessedYnabTransactionId = local.Id,
                NormalizedPayee = local.NormalizedPayee,
                Direction = local.Direction,
                SelectedCategoryId = categoryId,
                RuleSource = createExplicitRule ? RuleSource.Explicit : RuleSource.None,
                IsManualObservation = true,
                Status = CategorizationDecisionStatus.ManualApplied,
                SampleSize = 1,
                Consistency = 1m,
                Reason = reason,
                CreatedAt = now
            };
            db.CategorizationDecisions.Add(manualDecision);
            local.Decisions.Add(manualDecision);
        }

        if (createExplicitRule && local.NormalizedPayee is not null)
        {
            var rule = await db.MerchantRules.SingleOrDefaultAsync(item =>
                item.IsExplicit &&
                item.NormalizedPayee == local.NormalizedPayee &&
                item.Direction == local.Direction,
                cancellationToken);
            if (rule is null)
            {
                db.MerchantRules.Add(new MerchantRule
                {
                    Id = Guid.NewGuid(),
                    NormalizedPayee = local.NormalizedPayee,
                    Direction = local.Direction,
                    CategoryId = categoryId,
                    IsExplicit = true,
                    IsEnabled = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                rule.CategoryId = categoryId;
                rule.IsEnabled = true;
                rule.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReconcileExternalCategoryAsync(
        ProcessedYnabTransaction local,
        Guid categoryId,
        string? categoryName,
        CancellationToken cancellationToken)
    {
        local.Status = TransactionProcessingStatus.Applied;
        local.CategorizedAt ??= DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var run = new ProcessingRun
        {
            Id = Guid.NewGuid(),
            StartedAt = now,
            CompletedAt = now,
            FetchedCount = 1,
            SkippedCount = 1
        };
        db.ProcessingRuns.Add(run);
        if (!local.Decisions.Any(item => item.Status == CategorizationDecisionStatus.Skipped && item.Reason.Contains("externally", StringComparison.OrdinalIgnoreCase)))
        {
            local.Decisions.Add(new CategorizationDecision
            {
                Id = Guid.NewGuid(),
                ProcessingRun = run,
                NormalizedPayee = local.NormalizedPayee,
                Direction = local.Direction,
                SelectedCategoryId = categoryId,
                RuleSource = RuleSource.None,
                Status = CategorizationDecisionStatus.Skipped,
                Reason = $"Transaction was categorized externally as {categoryName ?? categoryId.ToString()}.",
                CreatedAt = now
            });
        }

        foreach (var pending in local.PendingUpdates.Where(item => item.Status != PendingUpdateStatus.Succeeded))
        {
            pending.Status = PendingUpdateStatus.Failed;
            pending.LastError = "Transaction was categorized externally.";
            pending.LastAttemptAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
