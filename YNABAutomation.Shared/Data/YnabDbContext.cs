using Microsoft.EntityFrameworkCore;
using YNABAutomationConsole.Categorization;

namespace YNABAutomationConsole.Data;

public sealed class YnabDbContext(DbContextOptions<YnabDbContext> options) : DbContext(options)
{
    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();
    public DbSet<ProcessedYnabTransaction> ProcessedYnabTransactions => Set<ProcessedYnabTransaction>();
    public DbSet<CategorizationDecision> CategorizationDecisions => Set<CategorizationDecision>();
    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();
    public DbSet<PendingCategoryUpdate> PendingCategoryUpdates => Set<PendingCategoryUpdate>();
    public DbSet<AiCategorizationDecision> AiCategorizationDecisions => Set<AiCategorizationDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessingRun>(entity =>
        {
            entity.HasKey(run => run.Id);
            entity.Property(run => run.StartedAt).IsRequired();
        });

        modelBuilder.Entity<ProcessedYnabTransaction>(entity =>
        {
            entity.HasKey(transaction => transaction.Id);
            entity.HasIndex(transaction => transaction.YnabTransactionId).IsUnique();
            entity.HasIndex(transaction => transaction.NormalizedPayee);
            entity.Property(transaction => transaction.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(transaction => transaction.PayeeName).HasMaxLength(500);
            entity.Property(transaction => transaction.NormalizedPayee).HasMaxLength(500);
            entity.Property(transaction => transaction.Memo).HasMaxLength(2000);
            entity.Property(transaction => transaction.AccountName).HasMaxLength(500);
        });

        modelBuilder.Entity<CategorizationDecision>(entity =>
        {
            entity.HasKey(decision => decision.Id);
            entity.HasIndex(decision => new { decision.NormalizedPayee, decision.SelectedCategoryId });
            entity.Property(decision => decision.RuleSource).HasConversion<string>().HasMaxLength(32);
            entity.Property(decision => decision.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(decision => new { decision.NormalizedPayee, decision.Direction, decision.SelectedCategoryId });
            entity.HasIndex(decision => decision.ProcessedYnabTransactionId)
                .IsUnique()
                .HasFilter("\"IsManualObservation\" = true AND \"Status\" = 'ManualApplied'");
            entity.Property(decision => decision.Consistency).HasPrecision(5, 4);
            entity.Property(decision => decision.Reason).HasMaxLength(2000);
            entity.HasOne(decision => decision.ProcessingRun)
                .WithMany()
                .HasForeignKey(decision => decision.ProcessingRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(decision => decision.ProcessedYnabTransaction)
                .WithMany(transaction => transaction.Decisions)
                .HasForeignKey(decision => decision.ProcessedYnabTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MerchantRule>(entity =>
        {
            entity.HasKey(rule => rule.Id);
            entity.HasIndex(rule => new { rule.NormalizedPayee, rule.Direction, rule.IsExplicit }).IsUnique();
            entity.Property(rule => rule.NormalizedPayee).HasMaxLength(500).IsRequired();
        });

        modelBuilder.Entity<PendingCategoryUpdate>(entity =>
        {
            entity.HasKey(update => update.Id);
            entity.HasIndex(update => new { update.ProcessedYnabTransactionId, update.Status });
            entity.Property(update => update.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(update => update.LastError).HasMaxLength(2000);
            entity.HasIndex(update => update.RequestId).IsUnique();
            entity.Property(update => update.RowVersion).IsRowVersion();
            entity.HasOne(update => update.ProcessedYnabTransaction)
                .WithMany(transaction => transaction.PendingUpdates)
                .HasForeignKey(update => update.ProcessedYnabTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiCategorizationDecision>(entity =>
        {
            entity.HasKey(decision => decision.Id);
            entity.HasIndex(decision => new { decision.ProcessedYnabTransactionId, decision.CreatedAt });
            entity.Property(decision => decision.Outcome).HasConversion<string>().HasMaxLength(32);
            entity.Property(decision => decision.Confidence).HasPrecision(5, 4);
            entity.Property(decision => decision.Reason).HasMaxLength(2000);
            entity.Property(decision => decision.FailureReason).HasMaxLength(2000);
            entity.Property(decision => decision.Model).HasMaxLength(200);
            entity.Property(decision => decision.ProposedCategoryName).HasMaxLength(500);
            entity.Property(decision => decision.AlternativeCategoryName).HasMaxLength(500);
            entity.HasOne(decision => decision.ProcessedYnabTransaction)
                .WithMany(transaction => transaction.AiDecisions)
                .HasForeignKey(decision => decision.ProcessedYnabTransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
