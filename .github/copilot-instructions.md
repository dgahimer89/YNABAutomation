# AI contributor guide

Start with [the architecture guide](../docs/architecture.md) to locate behavior,
then use [the development workflows](../docs/development-workflows.md) for
configuration, migrations, and validation. Keep these documents concise and
source-aligned; link to implementation rather than copying details likely to
drift.

## Repository map

- `YNABAutomation.Shared/Categorization` contains transaction eligibility,
  payee normalization, rule selection, AI fallback, transfer matching, and
  remote-update orchestration.
- `YNABAutomation.Shared/Ynab` contains the API contract, HTTP client,
  authentication handler, options, requests, and response models.
- `YNABAutomation.Shared/Data` contains the EF Core context, entities, and
  migrations.
- `YNABAutomationWeb` is the Razor Pages review and administration application.
- `YNABAutomationConsole` runs one categorization batch at startup.
- `YNABAutomationConsole.Tests` contains MSTest coverage using in-memory EF and
  fake API/AI collaborators.

Keep reusable behavior in `YNABAutomation.Shared`; do not duplicate it in either
entry point. The web and console applications have different composition roots,
so configuration or startup changes may need to be made in both `Program.cs`
files and both `appsettings.json` files.

## Change-navigation rules

- Categorization decisions: begin with
  `YNABAutomation.Shared/Categorization/YnabCategorizationProcessor.cs`, then
  follow the policy, selector, normalizer, and writer collaborators.
- API behavior: update the interface, client, request/model types, and
  `YnabApiClientTests.cs` together.
- Persisted state: update entities, `YnabDbContext`, and an EF migration; do not
  edit generated migration designer or snapshot content independently.
- Web review behavior: update the Razor Page model and its `.cshtml` view
  together, then check the corresponding shared service and entity workflow.
- New decision behavior: add focused tests in
  `YNABAutomationConsole.Tests` before changing auto-apply behavior.

Preserve existing enum values and persisted status meanings unless a migration
and all affected workflows are intentionally updated. Use existing helpers and
DI registrations before adding parallel abstractions.

## Safety invariants

- Never commit YNAB, OpenAI, or database credentials.
- Categorization is dry-run by default; do not silently make remote writes
  unconditional.
- Explicit rules take precedence over learned rules. Learned rules require both
  the configured sample-size and consistency thresholds.
- Transfers, deleted transactions, and transactions without a payee remain
  outside normal categorization.
- AI suggestions must be constrained to currently allowed YNAB categories and
  audited before auto-apply.
- Pending remote updates are persisted and retried; preserve this behavior when
  changing update orchestration.

## Validation

Use the existing targeted test project:

```powershell
dotnet test YNABAutomationConsole.Tests/YNABAutomationConsole.Tests.csproj
```

When changing configuration, startup, migrations, or shared public contracts,
also build the solution and inspect both application composition roots.

## Documentation and tests

Every change must keep the relevant documentation up to date. Update the
appropriate README, architecture/workflow guide, or AI contributor guidance
when behavior, configuration, source locations, or workflows change.

Keep tests up to date as appropriate for every change. Add or update focused
tests when behavior changes, and adjust existing tests when contracts,
configuration, persistence, or expected workflows change.

## YNAB API client configuration

The console application registers `IYnabApiClient` through asynchronous
`AddYnabApi(...)`. The client uses the YNAB API at
`https://api.ynab.com/v1/` and receives its bearer token through DI-backed
`YnabOptions`; the API client does not read secrets directly. When no plan ID is
configured, registration performs plan discovery before the service provider is
built, so connection and plan-count errors occur during startup.

Configure the YNAB client under the `Ynab` configuration section using
`ApiKey`, `PlanId`, and `BaseUrl`. `PlanId` is optional when the account has
exactly one plan; `AddYnabApi(...)` performs `GET /plans` discovery during
registration otherwise. No secret values should be committed to source control.

The application registers `YnabDbContext` for PostgreSQL. Configure the database
through the `ConnectionStrings:DefaultConnection` setting. The default host,
port, and database are used when no connection string is configured. Supply
credentials through user secrets or environment variables rather than
committing them to source control.
