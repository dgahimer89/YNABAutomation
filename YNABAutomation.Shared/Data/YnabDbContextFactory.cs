using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YNABAutomationConsole.Data;

public sealed class YnabDbContextFactory : IDesignTimeDbContextFactory<YnabDbContext>
{
    public YnabDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<YnabDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=ynabautomation")
            .Options;
        return new YnabDbContext(options);
    }
}
