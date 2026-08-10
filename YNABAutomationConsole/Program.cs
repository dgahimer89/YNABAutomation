using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

internal static class Program
{
    private static async Task Main()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ynabautomation";

        services.AddDbContext<YnabDbContext>(options => options.UseNpgsql(connectionString));
        await services.AddYnabApi(configuration);
        services.AddCategorization(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        try
        {
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<YnabDbContext>();
                await db.Database.MigrateAsync();

                var processor = scope.ServiceProvider.GetRequiredService<YnabCategorizationProcessor>();
                var result = await processor.ProcessAsync();
                Console.WriteLine(
                    $"Categorization complete: fetched={result.Fetched}, applied={result.Applied}, proposed={result.Proposed}, " +
                    $"review={result.ReviewRequired}, skipped={result.Skipped}, failed={result.Failed}.");
            }
        }
        catch (DbUpdateException exception)
        {
            Console.Error.WriteLine("Database update failed:");
            Console.Error.WriteLine(exception.InnerException?.Message ?? exception.Message);
            throw;
        }

        Console.WriteLine($"YNAB API client configured for plan '{configuration["Ynab:PlanId"] ?? "not configured"}'.");
    }
}
