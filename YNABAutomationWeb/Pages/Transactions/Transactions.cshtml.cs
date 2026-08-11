using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationWeb.Pages;

public sealed class TransactionsModel(
    YnabDbContext db,
    ManualTransactionResolutionService resolutionService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    public IReadOnlyList<ProcessedYnabTransaction> Transactions { get; private set; } = [];
    public IReadOnlyList<CategoryGroup> CategoryGroups { get; private set; } = [];
    public IReadOnlyList<Category> Categories { get; private set; } = [];

    [BindProperty]
    public Dictionary<string, string?> CategorySelections { get; set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostBulkCategorizeAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        var assignments = new List<(string TransactionId, Guid CategoryId)>();
        foreach (var selection in CategorySelections)
        {
            if (string.IsNullOrWhiteSpace(selection.Value))
            {
                continue;
            }

            if (!Guid.TryParse(selection.Value, out var categoryId) ||
                Categories.All(category => category.Id != categoryId))
            {
                ModelState.AddModelError(nameof(CategorySelections), "Select a valid current YNAB category.");
                continue;
            }

            assignments.Add((selection.Key, categoryId));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (assignments.Count == 0)
        {
            return RedirectToPage(new { message = "No transactions were categorized." });
        }

        var outcomes = new List<ManualResolutionOutcome>(assignments.Count);
        foreach (var assignment in assignments.DistinctBy(item => item.TransactionId, StringComparer.Ordinal))
        {
            outcomes.Add(await resolutionService.ResolveAsync(
                assignment.TransactionId,
                assignment.CategoryId,
                createExplicitRule: false,
                cancellationToken: cancellationToken));
        }

        var applied = outcomes.Count(outcome => outcome.Result == ManualResolutionResult.Applied);
        var alreadyResolved = outcomes.Count(outcome => outcome.Result == ManualResolutionResult.AlreadyResolved);
        var unsuccessful = outcomes.Count - applied - alreadyResolved;
        var message = $"Bulk categorization complete: {applied} categorized";
        if (alreadyResolved > 0)
        {
            message += $", {alreadyResolved} already resolved";
        }

        if (unsuccessful > 0)
        {
            message += $", {unsuccessful} not categorized";
        }

        return RedirectToPage(new { message });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Transactions = await db.ProcessedYnabTransactions
            .AsNoTracking()
            .Where(transaction => transaction.Status == TransactionProcessingStatus.ReviewRequired)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.PayeeName)
            .ToListAsync(cancellationToken);
        CategoryGroups = await resolutionService.GetValidCategoryGroupsAsync(cancellationToken);
        Categories = CategoryGroups.SelectMany(group => group.Categories).ToArray();
    }
}
