using Microsoft.EntityFrameworkCore;
using Serilog;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((context, _, loggerConfiguration) =>
            loggerConfiguration.ReadFrom.Configuration(context.Configuration));

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=ynabautomation";
        builder.Services.AddDbContext<YnabDbContext>(options => options.UseNpgsql(connectionString));
        await builder.Services.AddYnabApi(builder.Configuration);
        builder.Services.AddCategorization(builder.Configuration);
        builder.Services.AddRazorPages();

        var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YnabDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Applying database migrations for the web application.");
            await db.Database.MigrateAsync();
        }

        app.MapRazorPages();
        app.Logger.LogInformation("YNAB Automation web application is starting.");
        await app.RunAsync();
    }
}
