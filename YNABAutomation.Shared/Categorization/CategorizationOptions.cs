namespace YNABAutomationConsole.Categorization;

public sealed class CategorizationOptions
{
    public const string SectionName = "Categorization";
    public bool DryRun { get; set; } = true;
    public int MinimumLearnedSampleSize { get; set; } = 3;
    public decimal MinimumLearnedConsistency { get; set; } = 0.8m;
}
