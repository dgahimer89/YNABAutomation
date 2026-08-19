using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationWeb.Pages.Transactions;

public sealed class ResolveModel(
    YnabDbContext db,
    ManualTransactionResolutionService resolutionService,
    YnabCategorizationProcessor categorizationProcessor,
    ILogger<ResolveModel> logger) : PageModel
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
    public TransferCandidate? TransferCandidate { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        LogTransactionStatus();
        return Transaction is null ? NotFound() : Page();
    }

    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Submitting manual resolution for transaction {TransactionId}.", Id);
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
            logger.LogInformation("Manual resolution completed for transaction {TransactionId}: {Result}.",
                Id, outcome.Result);
            await categorizationProcessor.ProcessAsync(
                new HashSet<string>(StringComparer.Ordinal) { Id },
                cancellationToken);
            return RedirectToPage("/Transactions/Transactions", new { message = outcome.Message });
        }

        await LoadAsync(cancellationToken);
        LogTransactionStatus();
        return Transaction is null ? NotFound() : Page();
    }

    private void LogTransactionStatus()
    {
        if (Transaction is not null)
        {
            logger.LogInformation(
                "Loading transaction {TransactionId} for review: date={Date}, payee='{PayeeName}', amount={Amount}, direction={Direction}.",
                Transaction.YnabTransactionId,
                Transaction.TransactionDate,
                Transaction.PayeeName,
                Transaction.Amount,
                Transaction.Direction);
        }
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
        TransferCandidate = Transaction is null
            ? null
            : await db.TransferCandidates.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.YnabTransactionId == Id, cancellationToken);
        if (SelectedCategoryId is null && AiSuggestion?.ProposedCategoryId is Guid suggestedCategoryId)
        {
            SelectedCategoryId = suggestedCategoryId;
        }
    }
}
