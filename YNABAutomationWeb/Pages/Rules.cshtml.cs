using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationWeb.Pages;

public sealed class RulesModel(YnabDbContext db, IYnabApiClient ynab) : PageModel
{
    [BindProperty]
    public Guid RuleId { get; set; }

    public IReadOnlyList<RuleDisplay> Rules { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken cancellationToken)
    {
        await SetEnabledAsync(true, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken cancellationToken)
    {
        await SetEnabledAsync(false, cancellationToken);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var rule = await db.MerchantRules.SingleOrDefaultAsync(item => item.Id == RuleId, cancellationToken);
        if (rule is not null)
        {
            db.MerchantRules.Remove(rule);
            await db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToPage();
    }

    private async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var rule = await db.MerchantRules.SingleOrDefaultAsync(item => item.Id == RuleId, cancellationToken);
        if (rule is not null)
        {
            rule.IsEnabled = enabled;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var categories = (await ynab.GetCategoriesAsync(cancellationToken)).Data.CategoryGroups
            .SelectMany(group => group.Categories)
            .ToDictionary(category => category.Id, category => category.Name);
        Rules = await db.MerchantRules
            .AsNoTracking()
            .Where(rule => rule.IsExplicit)
            .OrderBy(rule => rule.NormalizedPayee)
            .Select(rule => new RuleDisplay(
                rule.Id,
                rule.NormalizedPayee,
                rule.Direction,
                rule.CategoryId,
                rule.IsEnabled))
            .ToListAsync(cancellationToken);
        foreach (var rule in Rules)
        {
            rule.CategoryName = categories.GetValueOrDefault(rule.CategoryId, "Unknown or deleted category");
        }
    }

    public sealed class RuleDisplay(
        Guid id,
        string normalizedPayee,
        TransactionDirection direction,
        Guid categoryId,
        bool isEnabled)
    {
        public Guid Id { get; } = id;
        public string NormalizedPayee { get; } = normalizedPayee;
        public TransactionDirection Direction { get; } = direction;
        public Guid CategoryId { get; } = categoryId;
        public bool IsEnabled { get; } = isEnabled;
        public string CategoryName { get; set; } = string.Empty;
    }
}
