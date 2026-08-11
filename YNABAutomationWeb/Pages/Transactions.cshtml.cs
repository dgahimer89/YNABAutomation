using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;

namespace YNABAutomationWeb.Pages;

public sealed class TransactionsModel(YnabDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    public IReadOnlyList<ProcessedYnabTransaction> Transactions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Transactions = await db.ProcessedYnabTransactions
            .AsNoTracking()
            .Where(transaction => transaction.Status == TransactionProcessingStatus.ReviewRequired)
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.PayeeName)
            .ToListAsync(cancellationToken);
    }
}
