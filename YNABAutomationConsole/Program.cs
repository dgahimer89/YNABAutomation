using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
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

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        var services = new ServiceCollection();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ynabautomation";

        services.AddDbContext<YnabDbContext>(options => options.UseNpgsql(connectionString));
        Log.Information("Configuring the YNAB API client.");
        await services.AddYnabApi(configuration);
        services.AddCategorization(configuration);
        services.AddSerilog(Log.Logger, dispose: true);

        using var serviceProvider = services.BuildServiceProvider();
        try
        {
            Log.Information("Starting YNAB categorization console job.");
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<YnabDbContext>();
                Log.Information("Applying database migrations.");
                await db.Database.MigrateAsync();

                var processor = scope.ServiceProvider.GetRequiredService<YnabCategorizationProcessor>();
                var result = await processor.ProcessAsync();
                Log.Information(
                    "Categorization complete: fetched={Fetched}, applied={Applied}, proposed={Proposed}, " +
                    "review={ReviewRequired}, skipped={Skipped}, failed={Failed}.",
                    result.Fetched,
                    result.Applied,
                    result.Proposed,
                    result.ReviewRequired,
                    result.Skipped,
                    result.Failed);
            }
            Log.Information("YNAB API client configured for plan '{PlanId}'.",
                configuration["Ynab:PlanId"] ?? "not configured");
        }
        catch (DbUpdateException exception)
        {
            Log.Error(exception, "Database update failed: {Message}",
                exception.InnerException?.Message ?? exception.Message);
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
