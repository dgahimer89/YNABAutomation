using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Data;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Tests;

[TestClass]
public sealed class AiCategorizationProcessorTests
{
    [TestMethod]
    public async Task ExplicitRule_PreventsOpenAiCall()
    {
        var categoryId = Guid.NewGuid();
        var (db, ynab, ai, processor) = CreateProcessor(new AiCategorizationResult(categoryId.ToString(), 1m, "test", null, false));
        db.MerchantRules.Add(new MerchantRule
        {
            Id = Guid.NewGuid(), NormalizedPayee = "merchant", CategoryId = categoryId,
            Direction = TransactionDirection.Outflow, IsExplicit = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await processor.ProcessAsync();

        Assert.AreEqual(0, ai.Calls);
    }

    [TestMethod]
    public async Task UnknownAiCategory_IsAuditedAndSentToReview()
    {
        var (_, _, ai, processor) = CreateProcessor(new AiCategorizationResult(Guid.NewGuid().ToString(), .99m, "test", null, false));

        var result = await processor.ProcessAsync();

        Assert.AreEqual(1, ai.Calls);
        Assert.AreEqual(1, result.ReviewRequired);
    }

    [TestMethod]
    public async Task HighConfidenceAiSuggestion_IsRevalidatedThenApplied()
    {
        var categoryId = Guid.NewGuid();
        var (_, ynab, _, processor) = CreateProcessor(new AiCategorizationResult(categoryId.ToString(), .99m, "test", null, false), categoryId);

        var result = await processor.ProcessAsync();

        Assert.AreEqual(1, ynab.GetTransactionCalls);
        Assert.AreEqual(1, ynab.UpdateCalls);
        Assert.AreEqual(1, result.Applied);
    }

    [TestMethod]
    public async Task LowConfidenceOrReviewAiSuggestion_DoesNotWriteToYnab()
    {
        var categoryId = Guid.NewGuid();
        var (_, ynab, _, processor) = CreateProcessor(new AiCategorizationResult(categoryId.ToString(), .94m, "test", null, false), categoryId);

        var result = await processor.ProcessAsync();

        Assert.AreEqual(0, ynab.UpdateCalls);
        Assert.AreEqual(1, result.ReviewRequired);
    }

    private static (YnabDbContext Db, FakeYnab Ynab, FakeAi Ai, YnabCategorizationProcessor Processor) CreateProcessor(
        AiCategorizationResult aiResult,
        Guid? allowedCategoryId = null)
    {
        var db = new YnabDbContext(new DbContextOptionsBuilder<YnabDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ynab = new FakeYnab(allowedCategoryId ?? Guid.NewGuid());
        var ai = new FakeAi(aiResult);
        var processor = new YnabCategorizationProcessor(
            db, ynab, new PayeeNormalizer(), new CategoryCandidateSelector(db),
            new AutoApplyPolicy(Options.Create(new CategorizationOptions())),
            Options.Create(new CategorizationOptions { DryRun = false }),
            Options.Create(new OpenAiOptions { AutoApplyConfidenceThreshold = .95m }),
            ai, new NullProposedChangeWriter());
        return (db, ynab, ai, processor);
    }

    private sealed class FakeAi(AiCategorizationResult result) : IAiCategorizer
    {
        public int Calls { get; private set; }
        public bool IsConfigured => true;
        public Task<AiCategorizationResult> CategorizeAsync(AiCategorizationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class NullProposedChangeWriter : IProposedChangeWriter
    {
        public void Write(string transactionId, string? payeeName, long amount, Guid categoryId, decimal? aiConfidence, string reason) { }
    }

    private sealed class FakeYnab(Guid categoryId) : IYnabApiClient
    {
        private readonly Transaction _transaction = new()
        {
            Id = "transaction-1", Date = new DateOnly(2026, 8, 15), Amount = -12500, PayeeName = "Merchant", AccountName = "Checking"
        };
        public int GetTransactionCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<PlansResponse> GetPlansAsync(GetPlansOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlansResponse { Data = new PlansData { Plans = [] } });
        public Task<TransactionsResponse> GetTransactionsAsync(GetTransactionsOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TransactionsResponse { Data = new TransactionsData { Transactions = [_transaction] } });
        public Task<TransactionResponse> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
        {
            GetTransactionCalls++;
            return Task.FromResult(new TransactionResponse { Data = new TransactionData { Transaction = _transaction } });
        }
        public Task<CategoriesResponse> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CategoriesResponse
            {
                Data = new CategoriesData
                {
                    CategoryGroups = [new CategoryGroup { Id = Guid.NewGuid(), Name = "Living", Categories = [new Category { Id = categoryId, Name = "Groceries" }] }]
                }
            });
        public Task<SaveTransactionsResponse> UpdateTransactionsAsync(IReadOnlyCollection<UpdateTransactionsRequest> transactions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransactionResponse> UpdateTransactionAsync(UpdateTransactionCategoryRequest transaction, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(new TransactionResponse { Data = new TransactionData { Transaction = _transaction } });
        }
    }
}
