# Architecture

## Solution boundaries

`YNABAutomation.Shared` is the reusable application core. It owns the YNAB HTTP
client, configuration options, EF Core persistence, migrations, and
categorization workflows. `YNABAutomationWeb` and `YNABAutomationConsole` are
composition roots that configure the shared services for different user
experiences.

The web app exposes Razor Pages for transaction review, merchant rules, and AI
decision history. The console app performs one migration-and-categorization run
when it starts. Neither entry point should reimplement shared categorization or
API behavior.

## Dependency flow

```text
Web / Console Program.cs
        |
        +--> service registration
        |      +--> YnabDbContext (PostgreSQL)
        |      +--> IYnabApiClient (YNAB HTTP API)
        |      +--> categorization services
        |
        +--> web pages or console batch
                |
                +--> YnabCategorizationProcessor
                        +--> eligibility and payee normalization
                        +--> explicit / learned rule selection
                        +--> optional OpenAI categorization
                        +--> transfer reconciliation
                        +--> persisted decisions and pending updates
                        +--> YNAB category updates
```

The implementation entry point for the main batch is
`YNABAutomation.Shared/Categorization/YnabCategorizationProcessor.cs`.
Registration is split across `CategorizationServiceCollectionExtensions.cs`,
`YnabServiceCollectionExtensions.cs`, and the two application `Program.cs`
files.

## Categorization decision flow

1. Fetch transactions using the configured YNAB query; the normal batch targets
   uncategorized transactions.
2. Classify eligibility. Inflows and outflows are supported; transfers, deleted
   transactions, and missing-payee transactions are skipped or sent to their
   dedicated workflow.
3. Normalize the payee with `PayeeNormalizer`.
4. Select an explicit merchant rule or a learned candidate using
   `RuleSelection`.
5. Apply `AutoApplyPolicy`. Learned candidates must satisfy minimum sample and
   consistency thresholds and must not be ambiguous.
6. If local rules do not resolve the transaction and OpenAI is configured, ask
   for a constrained category suggestion. Store the result in
   `AiCategorizationDecision`.
7. Revalidate high-confidence AI suggestions against current YNAB data before
   writing. Lower-confidence, invalid, ambiguous, or failed suggestions remain
   in review.
8. Persist the transaction, decision, run result, and pending update before the
   remote write. Retry pending or failed updates on a later startup.

The safety policy is implemented in the categorization classes, not in the UI.
Changes to thresholds or auto-apply rules should be accompanied by focused tests.

## Persistence model

`YnabDbContext` defines the durable workflow:

- `ProcessingRun` records batch totals.
- `ProcessedYnabTransaction` is the local identity and status record for a YNAB
  transaction.
- `CategorizationDecision` records rule source, status, confidence data, and
  rationale.
- `MerchantRule` stores explicit and learned merchant/category mappings.
- `PendingCategoryUpdate` provides durable remote-write retry state.
- `AiCategorizationDecision` provides an audit trail for AI outcomes.
- `TransferCandidate` tracks transfer matching and repair state.

Database shape and indexes are defined in
`YNABAutomation.Shared/Data/YnabDbContext.cs`. Schema changes must be represented
by a new migration under `YNABAutomation.Shared/Data/Migrations`; the model
snapshot and designer files are generated outputs.

## API boundary

`IYnabApiClient` is the seam used by categorization and tests. `YnabApiClient`
translates typed requests to the YNAB REST API, while
`YnabAuthenticationHandler` adds the bearer token. Keep secrets in options and
configuration; the client must not read files or environment variables directly.
