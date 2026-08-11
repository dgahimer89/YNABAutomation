using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace YNABAutomationWeb.Pages;

public sealed class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        return LocalRedirect("~/Transactions");
    }
}
