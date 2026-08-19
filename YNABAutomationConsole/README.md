# YNAB API client

The console application registers `IYnabApiClient` through asynchronous `AddYnabApi(...)`. The client uses the YNAB API at `https://api.ynab.com/v1/` and receives its bearer token through DI-backed `YnabOptions`; the API client does not read secrets directly. When no plan ID is configured, registration performs plan discovery before the service provider is built, so connection and plan-count errors occur during startup.

Configure the YNAB client under the `Ynab` configuration section using `ApiKey`,
`PlanId`, and `BaseUrl`. `PlanId` is optional when the account has exactly one
plan; `AddYnabApi(...)` performs `GET /plans` discovery during registration
otherwise. No secret values should be committed to source control.

The application registers `YnabDbContext` for PostgreSQL. Configure the database through the `ConnectionStrings:DefaultConnection` setting, for example:

`Host=localhost;Port=5432;Database=ynabautomation;Username=postgres;Password=your-password`

The default host, port, and database are used when no connection string is configured. Supply credentials through user secrets or environment variables rather than committing them to source control.

## Deterministic categorization

Each startup runs one categorization batch. The processor requests transactions with YNAB's `type=uncategorized` filter; it does not download categorized transactions. Inflows and outflows are supported, while transfers, deleted transactions, and transactions without a payee are recorded as skipped.

Configure learned-rule safety thresholds under the `Categorization` section:

- `DryRun` defaults to `true` for development safety. In this mode, proposed category changes are printed to the console and recorded locally, but no YNAB update request is sent and no pending update is created. Set `Categorization:DryRun=false` only when ready to apply changes.
- `MinimumLearnedSampleSize` defaults to `3`.
- `MinimumLearnedConsistency` defaults to `0.8` (80%).

Explicit merchant rules are stored in `MerchantRules` with `IsExplicit=true`, a normalized payee, and a YNAB category ID. Explicit rules take precedence over learned rules. Learned rules use only prior successful automatic categorizations; ambiguous or insufficient history is recorded as `ReviewRequired` without changing YNAB.

The processor persists the transaction, decision, processing run, and pending update before calling YNAB. Pending or failed updates are retried on the next startup, which handles a successful remote update followed by a local database failure without requiring categorized transactions to be fetched. On startup, EF Core applies all pending migrations with `Database.MigrateAsync()` before processing begins. A database previously created with `EnsureCreated` does not have migration history; for development, recreate that database or baseline it before using the migration-based startup.

## OpenAI categorization

When neither an explicit nor a sufficiently reliable learned rule resolves an eligible transaction, the processor can request a constrained OpenAI Responses API categorization. Set `OpenAI:ApiKey` through user secrets or an environment variable (for example, `OpenAI__ApiKey`); never commit the key. The default model is `gpt-5-mini`, and `OpenAI:AutoApplyConfidenceThreshold` defaults to `0.95`.

OpenAI receives only the current transaction details, a small relevant manual-history summary, and the currently allowed YNAB categories. Its structured response is rejected when it names a category outside that supplied list. Every request outcome is stored in the AI decision audit history. High-confidence suggestions are written through the persisted pending-update workflow without a per-transaction pre-write re-check; lower-confidence, ambiguous, invalid, or failed requests remain in the existing review workflow.

Tests use MSTest and can be run with:

`dotnet test YNABAutomationConsole.Tests/YNABAutomationConsole.Tests.csproj`

For AI-assisted maintenance, see the repository's [AI contributor guide](../.github/copilot-instructions.md),
[architecture guide](../docs/architecture.md), and [development workflows](../docs/development-workflows.md).
