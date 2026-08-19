using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;

namespace YNABAutomationWeb.Pages;

public sealed class AiDecisionsModel(
    YnabDbContext db,
    ILogger<AiDecisionsModel> logger) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AiDecisionRow> Decisions { get; private set; } = [];
    public int TotalDecisionCount { get; private set; }
    public int TotalPages { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading AI decision history page {PageNumber}.", PageNumber);
        PageNumber = Math.Max(1, PageNumber);
        TotalDecisionCount = await db.AiCategorizationDecisions
            .CountAsync(cancellationToken);
        TotalPages = (int)Math.Ceiling(TotalDecisionCount / (double)PageSize);
        PageNumber = TotalPages == 0 ? 1 : Math.Min(PageNumber, TotalPages);

        var rows = await db.AiCategorizationDecisions
            .AsNoTracking()
            .Include(decision => decision.ProcessedYnabTransaction)
            .OrderByDescending(decision => decision.ProcessedYnabTransaction!.TransactionDate)
            .ThenByDescending(decision => decision.CreatedAt)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
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
        Decisions = rows;
        logger.LogInformation("Loaded {Count} AI decisions for page {PageNumber}.",
            Decisions.Count, PageNumber);
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
