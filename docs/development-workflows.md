# Development workflows

## Configuration and secrets

Shared defaults live in each application's `appsettings.json`. Use .NET user
secrets or environment variables for `Ynab:ApiKey`, `Ynab:PlanId`,
`ConnectionStrings:DefaultConnection`, and `OpenAI:ApiKey`. Nested environment
variables use double underscores, such as `OpenAI__ApiKey`.

The web and console projects share a `UserSecretsId`, but both composition roots
must be checked when changing configuration behavior. Never place real tokens or
database credentials in source, tests, or documentation.

## Running the applications

Start the review UI:

```powershell
dotnet run --project YNABAutomationWeb
```

Run one console categorization batch:

```powershell
dotnet run --project YNABAutomationConsole
```

Both applications apply pending EF migrations during startup. The console then
processes one batch; the web app starts the Razor Pages host.

## Categorization safety

`Categorization:DryRun` defaults to `true`. Dry-run records and reports proposed
changes without sending a category update to YNAB. Set it to `false` only when
the proposed behavior is understood.

`MinimumLearnedSampleSize` and `MinimumLearnedConsistency` protect learned
rules. `OpenAI:AutoApplyConfidenceThreshold` controls the confidence threshold
for AI auto-apply, but AI output must also be valid, not marked for review, and
revalidated against YNAB. Preserve these gates when changing processor logic.

## Database and migrations

Use PostgreSQL for normal application runs. Create schema changes by updating
the model and adding an EF migration from the shared project; do not hand-edit
the model snapshot or generated designer files. Review the generated migration
for destructive operations and update related tests or startup assumptions.

Startup uses `Database.MigrateAsync()`. A database created with
`EnsureCreated` has no migration history and must be recreated or baselined
before migration-based startup can manage it.

## Remote update and retry workflow

The processor persists a pending category update before calling YNAB. A later
startup retries pending or failed updates, which protects against a remote
success followed by a local persistence failure. Preserve request identity,
status transitions, attempt counts, and error information when modifying this
path.

## Tests and change validation

Run the existing MSTest project:

```powershell
dotnet test YNABAutomationConsole.Tests/YNABAutomationConsole.Tests.csproj
```

Use in-memory EF for persistence-focused unit tests and fake `IYnabApiClient` or
`IAiCategorizer` implementations for decision workflows. API client tests use a
recording HTTP handler to assert URLs, headers, request bodies, and error
translation without contacting YNAB.

Common validation targets:

- Rule, eligibility, normalization, transfer, or AI threshold changes:
  `CategorizationTests.cs` and `AiCategorizationProcessorTests.cs`.
- Request, authentication, plan discovery, or API error changes:
  `YnabApiClientTests.cs`.
- Entity or migration changes: targeted workflow tests plus a solution build.
- Razor Page changes: inspect both the page model and view, then build the web
  project.

## Change checklist

Before finishing a change, identify the shared service that owns the behavior,
update both composition roots when configuration/startup is affected, preserve
secret handling and safety gates, add or update focused tests, and update
documentation links when a workflow or source location changes.
