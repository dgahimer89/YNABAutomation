using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YNABAutomationConsole.Categorization;
using YNABAutomationConsole.Ynab;

namespace YNABAutomationConsole.Tests;

[TestClass]
public sealed class CategorizationTests
{
    [TestMethod]
    public void NormalizePayee_IsConservativeAndStable()
    {
        var normalizer = new PayeeNormalizer();

        Assert.AreEqual("cafe central 123", normalizer.Normalize(" Café Central #123 "));
        Assert.AreEqual("cafe central 123", normalizer.Normalize("cafe-central 123"));
        Assert.IsNull(normalizer.Normalize("   "));
    }

    [TestMethod]
    public void Classifier_AllowsInflowsAndOutflows()
    {
        var inflow = TransactionClassifier.Classify(new Transaction { Amount = 100, PayeeName = "Employer" });
        var outflow = TransactionClassifier.Classify(new Transaction { Amount = -100, PayeeName = "Merchant" });

        Assert.IsTrue(inflow.IsEligible);
        Assert.IsTrue(inflow.IsInflow);
        Assert.IsTrue(outflow.IsEligible);
        Assert.IsFalse(outflow.IsInflow);
    }

    [TestMethod]
    public void Classifier_ExcludesTransfers()
    {
        var result = TransactionClassifier.Classify(new Transaction
        {
            Amount = -100,
            PayeeName = "Checking",
            TransferAccountId = Guid.NewGuid()
        });

        Assert.IsFalse(result.IsEligible);
        Assert.IsTrue(result.IsTransfer);
    }

    [TestMethod]
    public void Policy_TrustsExplicitRules()
    {
        var policy = new AutoApplyPolicy(Options.Create(new CategorizationOptions
        {
            MinimumLearnedSampleSize = 10,
            MinimumLearnedConsistency = 1m
        }));

        var allowed = policy.CanAutoApply(
            new CategoryCandidate(Guid.NewGuid(), RuleSource.Explicit, 0, 1m, false, "explicit"),
            out _);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public void Policy_RequiresLearnedSampleAndConsistencyThresholds()
    {
        var policy = new AutoApplyPolicy(Options.Create(new CategorizationOptions
        {
            MinimumLearnedSampleSize = 3,
            MinimumLearnedConsistency = 0.8m
        }));

        Assert.IsFalse(policy.CanAutoApply(
            new CategoryCandidate(Guid.NewGuid(), RuleSource.Learned, 2, 1m, false, "small"), out _));
        Assert.IsFalse(policy.CanAutoApply(
            new CategoryCandidate(Guid.NewGuid(), RuleSource.Learned, 3, 0.79m, false, "inconsistent"), out _));
        Assert.IsTrue(policy.CanAutoApply(
            new CategoryCandidate(Guid.NewGuid(), RuleSource.Learned, 3, 0.8m, false, "reliable"), out _));
    }

    [TestMethod]
    public void Policy_RejectsAmbiguousCandidates()
    {
        var policy = new AutoApplyPolicy(Options.Create(new CategorizationOptions()));

        Assert.IsFalse(policy.CanAutoApply(
            new CategoryCandidate(null, RuleSource.Learned, 6, 0.5m, true, "tie"), out _));
    }

    [TestMethod]
    public void UncategorizedRequest_UsesOnlyUncategorizedType()
    {
        var options = new GetTransactionsOptions { Type = TransactionType.Uncategorized };

        Assert.AreEqual(TransactionType.Uncategorized, options.Type);
    }

    [TestMethod]
    public void OpenAiThreshold_ChangesAutomaticEligibility()
    {
        const decimal confidence = 0.94m;
        var strict = new OpenAiOptions { AutoApplyConfidenceThreshold = 0.95m };
        var permissive = new OpenAiOptions { AutoApplyConfidenceThreshold = 0.9m };

        Assert.IsFalse(confidence >= strict.AutoApplyConfidenceThreshold);
        Assert.IsTrue(confidence >= permissive.AutoApplyConfidenceThreshold);
    }

    [TestMethod]
    public void OpenAiReviewRequest_PreventsAutomaticEligibility()
    {
        var result = new AiCategorizationResult(
            Guid.NewGuid().ToString(),
            1m,
            "Ambiguous merchant.",
            null,
            true);

        var canAutoApply = !result.RequiresReview && result.Confidence >= 0.95m;

        Assert.IsFalse(canAutoApply);
    }

    [TestMethod]
    public void OpenAiResult_CanRepresentAnExplicitUnresolvedSuggestion()
    {
        var result = new AiCategorizationResult(null, 0.2m, "Insufficient evidence.", null, true);

        Assert.IsNull(result.CategoryId);
        Assert.IsTrue(result.RequiresReview);
    }

    [TestMethod]
    public void TransferMatcher_RequiresDifferentAccountsExactAmountsAndClearedSides()
    {
        var source = new Transaction
        {
            Id = "source",
            AccountId = Guid.NewGuid(),
            Date = new DateOnly(2026, 8, 10),
            Amount = -500000,
            Cleared = "cleared"
        };
        var counterpart = new Transaction
        {
            Id = "counterpart",
            AccountId = Guid.NewGuid(),
            Date = new DateOnly(2026, 8, 12),
            Amount = 500000,
            Cleared = "cleared"
        };

        Assert.AreEqual(1, TransferMatcher.FindMatches(source, [source, counterpart], 3).Count);
        Assert.AreEqual(0, TransferMatcher.FindMatches(
            source, [source, new Transaction
            {
                Id = counterpart.Id, AccountId = counterpart.AccountId, Date = counterpart.Date,
                Amount = 499999, Cleared = counterpart.Cleared
            }], 3).Count);
        Assert.AreEqual(0, TransferMatcher.FindMatches(
            source, [source, new Transaction
            {
                Id = counterpart.Id, AccountId = source.AccountId, Date = counterpart.Date,
                Amount = counterpart.Amount, Cleared = counterpart.Cleared
            }], 3).Count);
        Assert.AreEqual(0, TransferMatcher.FindMatches(
            source, [source, new Transaction
            {
                Id = counterpart.Id, AccountId = counterpart.AccountId, Date = counterpart.Date,
                Amount = counterpart.Amount, Cleared = "uncleared"
            }], 3).Count);
    }

    [TestMethod]
    public void TransferMatcher_RejectsDatesOutsideConfiguredWindow()
    {
        var source = new Transaction
        {
            Id = "source", AccountId = Guid.NewGuid(), Date = new DateOnly(2026, 8, 10),
            Amount = -500000, Cleared = "cleared"
        };
        var counterpart = new Transaction
        {
            Id = "counterpart", AccountId = Guid.NewGuid(), Date = new DateOnly(2026, 8, 14),
            Amount = 500000, Cleared = "cleared"
        };

        Assert.AreEqual(0, TransferMatcher.FindMatches(source, [source, counterpart], 3).Count);
        Assert.AreEqual(1, TransferMatcher.FindMatches(source, [source, counterpart], 4).Count);
    }
}
