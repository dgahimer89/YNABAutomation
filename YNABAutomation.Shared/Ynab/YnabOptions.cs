namespace YNABAutomationConsole.Ynab;

public sealed class YnabOptions
{
    public const string SectionName = "Ynab";

    public string BaseUrl { get; set; } = "https://api.ynab.com/v1/";

    public string ApiKey { get; set; } = string.Empty;

    public string? PlanId { get; set; }
}
