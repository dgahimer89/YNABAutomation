# YNAB Automation

YNAB Automation helps categorize uncategorized transactions in a YNAB budget. It
downloads pending transactions, normalizes merchant names, and applies an
explicit merchant rule or a sufficiently consistent learned rule when one is
available. Transactions that cannot be categorized safely remain available for
manual review. Optional OpenAI categorization can suggest a category when the
local rules do not provide an answer; suggestions and outcomes are retained in
the local database for review.

The solution contains two entry points:

- **Web application** (`YNABAutomationWeb`) - a Razor Pages interface for
  reviewing pending transactions, assigning categories, managing merchant
  rules, and reviewing AI decisions.
- **Console application** (`YNABAutomationConsole`) - runs one categorization
  batch at startup, applies database migrations, retries pending updates, and
  reports the results to the console.

Both applications use PostgreSQL for local transaction, decision, rule, and
pending-update data and communicate with the YNAB API. Categorization defaults
to dry-run mode, so set `Categorization:DryRun` to `false` only after reviewing
the proposed changes.

## Configuration

Shared non-secret defaults are in each project's `appsettings.json`. The
database connection can be overridden with
`ConnectionStrings:DefaultConnection`. The YNAB API uses the `Ynab` section,
and OpenAI categorization uses the `OpenAI` section. Environment variables use
double underscores for nested settings, for example `OpenAI__ApiKey`.

## User secrets file

The projects use the .NET user-secrets feature so API keys and database
credentials stay outside the repository. Both projects share the same
`UserSecretsId`, so the same secret store can be used when running either
application. On Windows, the file is normally:

`%APPDATA%\Microsoft\UserSecrets\5d16ba59-8399-4e8e-a807-61f0d306108c\secrets.json`

Create or update values with the .NET CLI instead of committing them to
`appsettings.json`:

```powershell
dotnet user-secrets --project YNABAutomationWeb set "Ynab:ApiKey" "YOUR_YNAB_TOKEN"
dotnet user-secrets --project YNABAutomationWeb set "Ynab:PlanId" "YOUR_PLAN_ID"
dotnet user-secrets --project YNABAutomationWeb set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=ynabautomation;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets --project YNABAutomationWeb set "OpenAI:ApiKey" "YOUR_OPENAI_KEY"
```

`Ynab:ApiKey` is the YNAB bearer token. `Ynab:PlanId` is optional; when it is
omitted, the application discovers the plan if the account has exactly one.
`ConnectionStrings:DefaultConnection` is the PostgreSQL connection string.
`OpenAI:ApiKey` enables optional AI categorization; if it is absent, the app
continues using local rules and manual review.

The secrets file is local machine configuration and is not encrypted by the
application. Protect it as sensitive data, do not check it into source
control, and do not place real values in documentation or committed settings.

## Running

Start the web interface with:

```powershell
dotnet run --project YNABAutomationWeb
```

Run one console categorization batch with:

```powershell
dotnet run --project YNABAutomationConsole
```

Tests use MSTest:

```powershell
dotnet test YNABAutomationConsole.Tests/YNABAutomationConsole.Tests.csproj
```
