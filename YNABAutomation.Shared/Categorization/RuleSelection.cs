using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YNABAutomationConsole.Data;

namespace YNABAutomationConsole.Categorization;

public sealed record CategoryCandidate(
    Guid? CategoryId,
    RuleSource Source,
    int SampleSize,
    decimal? Consistency,
    bool IsAmbiguous,
    string Reason);

public sealed class CategoryCandidateSelector(YnabDbContext db)
{
    public async Task<CategoryCandidate> SelectAsync(
        string normalizedPayee,
        TransactionDirection direction = TransactionDirection.Outflow,
        CancellationToken cancellationToken = default)
    {
        var explicitRule = await db.MerchantRules
            .AsNoTracking()
            .SingleOrDefaultAsync(rule => rule.IsExplicit && rule.IsEnabled
                && rule.Direction == direction && rule.NormalizedPayee == normalizedPayee,
                cancellationToken);
        if (explicitRule is not null)
        {
            return new(explicitRule.CategoryId, RuleSource.Explicit, 0, 1m, false, "Explicit merchant rule.");
        }

        var history = await db.CategorizationDecisions
            .AsNoTracking()
            .Where(decision => decision.NormalizedPayee == normalizedPayee
                && decision.Direction == direction
                && decision.SelectedCategoryId != null
                && decision.IsManualObservation
                && decision.Status == CategorizationDecisionStatus.ManualApplied)
            .GroupBy(decision => decision.SelectedCategoryId)
            .Select(group => new CategoryCount(group.Key!.Value, group.Count()))
            .ToListAsync(cancellationToken);

        if (history.Count == 0)
        {
            return new(null, RuleSource.None, 0, null, false, "No explicit rule or categorization history.");
        }

        var total = history.Sum(item => item.Count);
        var top = history.OrderByDescending(item => item.Count).ThenBy(item => item.CategoryId).ToArray();
        var consistency = (decimal)top[0].Count / total;
        if (top.Length > 1 && top[0].Count == top[1].Count)
        {
            return new(null, RuleSource.Learned, total, consistency, true, "Multiple categories are equally common.");
        }

        return new(top[0].CategoryId, RuleSource.Learned, total, consistency, false, "Learned from prior auto-applied decisions.");
    }

    private sealed record CategoryCount(Guid CategoryId, int Count);
}

public sealed class AutoApplyPolicy(IOptions<CategorizationOptions> options)
{
    private readonly CategorizationOptions _options = options.Value;

    public bool CanAutoApply(CategoryCandidate candidate, out string reason)
    {
        if (candidate.CategoryId is null)
        {
            reason = "No category candidate was found.";
            return false;
        }

        if (candidate.IsAmbiguous)
        {
            reason = "The category candidate is ambiguous.";
            return false;
        }

        if (candidate.Source == RuleSource.Explicit)
        {
            reason = "Explicit merchant rule is trusted.";
            return true;
        }

        if (candidate.SampleSize < _options.MinimumLearnedSampleSize)
        {
            reason = $"Learned rule has {candidate.SampleSize} samples; {_options.MinimumLearnedSampleSize} required.";
            return false;
        }

        if (candidate.Consistency < _options.MinimumLearnedConsistency)
        {
            reason = $"Learned rule consistency is {candidate.Consistency:P1}; {_options.MinimumLearnedConsistency:P1} required.";
            return false;
        }

        reason = "Learned rule meets the configured safety thresholds.";
        return true;
    }
}
