using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(builder.Configuration["Urls:Web"] ?? "http://localhost:5000");

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
            await db.Database.MigrateAsync();
        }

        app.MapRazorPages();
        await app.RunAsync();
    }
}
