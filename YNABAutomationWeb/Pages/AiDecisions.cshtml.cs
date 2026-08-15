using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;

namespace YNABAutomationWeb.Pages;

public sealed class AiDecisionsModel(YnabDbContext db) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AiDecisionRow> Decisions { get; private set; } = [];
    public bool HasNextPage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PageNumber = Math.Max(1, PageNumber);
        var rows = await db.AiCategorizationDecisions
            .AsNoTracking()
            .Include(decision => decision.ProcessedYnabTransaction)
            .OrderByDescending(decision => decision.CreatedAt)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize + 1)
            .Select(decision => new AiDecisionRow(
                decision.ProcessedYnabTransaction!.YnabTransactionId,
                decision.ProcessedYnabTransaction.TransactionDate,
                decision.ProcessedYnabTransaction.PayeeName,
                decision.ProcessedYnabTransaction.Amount,
                decision.ProcessedYnabTransaction.Direction,
                decision.ProposedCategoryName,
                decision.Confidence,
                decision.Reason,
                decision.AlternativeCategoryName,
                decision.Model,
                decision.RequiresReview,
                decision.MetAutoApplyThreshold,
                decision.Outcome,
                decision.FinalCategoryId,
                decision.FailureReason))
            .ToListAsync(cancellationToken);
        HasNextPage = rows.Count > PageSize;
        Decisions = rows.Take(PageSize).ToArray();
    }

    public sealed record AiDecisionRow(
        string TransactionId,
        DateOnly TransactionDate,
        string? Payee,
        long Amount,
        TransactionDirection Direction,
        string? SuggestedCategory,
        decimal? Confidence,
        string Reason,
        string? AlternativeCategory,
        string Model,
        bool RequiresReview,
        bool MetThreshold,
        AiDecisionOutcome Outcome,
        Guid? FinalCategoryId,
        string? FailureReason);
}
