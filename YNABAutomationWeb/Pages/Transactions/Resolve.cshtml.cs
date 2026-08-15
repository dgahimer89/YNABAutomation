using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationWeb.Pages.Transactions;

public sealed class ResolveModel(
    YnabDbContext db,
    ManualTransactionResolutionService resolutionService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public Guid? SelectedCategoryId { get; set; }

    [BindProperty]
    public bool AlwaysCategorizeMerchant { get; set; }

    public ProcessedYnabTransaction? Transaction { get; private set; }
    public IReadOnlyList<CategoryGroup> CategoryGroups { get; private set; } = [];
    public IReadOnlyList<Category> Categories { get; private set; } = [];
    public IReadOnlyList<CategorizationDecision> ManualHistory { get; private set; } = [];
    public AiCategorizationDecision? AiSuggestion { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Transaction is null ? NotFound() : Page();
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (SelectedCategoryId is null)
        {
            ModelState.AddModelError(nameof(SelectedCategoryId), "Select a category.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var outcome = await resolutionService.ResolveAsync(
            Id,
            SelectedCategoryId!.Value,
            AlwaysCategorizeMerchant,
            cancellationToken);
        Message = outcome.Message;
        if (outcome.Result is ManualResolutionResult.Applied or ManualResolutionResult.AlreadyResolved)
        {
            return RedirectToPage("/Transactions/Transactions", new { message = outcome.Message });
        }

        await LoadAsync(cancellationToken);
        return Transaction is null ? NotFound() : Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Transaction = await db.ProcessedYnabTransactions
            .AsNoTracking()
            .Include(transaction => transaction.Decisions)
            .Include(transaction => transaction.AiDecisions)
            .SingleOrDefaultAsync(transaction => transaction.YnabTransactionId == Id, cancellationToken);
        CategoryGroups = await resolutionService.GetValidCategoryGroupsAsync(cancellationToken);
        Categories = CategoryGroups.SelectMany(group => group.Categories).ToArray();
        ManualHistory = Transaction?.Decisions
            .Where(decision => decision.IsManualObservation && decision.Status == CategorizationDecisionStatus.ManualApplied)
            .OrderByDescending(decision => decision.CreatedAt)
            .ToArray() ?? [];
        AiSuggestion = Transaction?.AiDecisions
            .Where(decision => decision.Outcome == AiDecisionOutcome.Suggested)
            .OrderByDescending(decision => decision.CreatedAt)
            .FirstOrDefault();
        if (SelectedCategoryId is null && AiSuggestion?.ProposedCategoryId is Guid suggestedCategoryId)
        {
            SelectedCategoryId = suggestedCategoryId;
        }
    }
}
