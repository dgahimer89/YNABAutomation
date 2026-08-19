namespace YNABAutomationConsole.Ynab;

public sealed class YnabOptions
{
    public const string SectionName = "Ynab";

    public string BaseUrl { get; set; } = "https://api.ynab.com/v1/";

    public string ApiKey { get; set; } = string.Empty;

    public string? PlanId { get; set; }

    public bool UseDateRange { get; set; }

    public DateOnly? SinceDate { get; set; }

    public DateOnly? UntilDate { get; set; }

    public (DateOnly SinceDate, DateOnly UntilDate) GetTransactionDateRange(DateOnly today)
    {
        var sinceDate = SinceDate ?? today.AddMonths(-1);
        var untilDate = UntilDate ?? today;
        if (sinceDate > untilDate)
        {
            throw new ArgumentException("Ynab:SinceDate must be on or before Ynab:UntilDate.");
        }

        return (sinceDate, untilDate);
    }
}
