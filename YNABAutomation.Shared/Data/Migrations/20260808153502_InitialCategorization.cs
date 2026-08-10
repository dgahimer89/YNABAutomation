using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomationConsole.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCategorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MerchantRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedPayee = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedYnabTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    YnabTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    PayeeName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NormalizedPayee = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsInflow = table.Column<bool>(type: "boolean", nullable: false),
                    IsTransfer = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CategorizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedYnabTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedCount = table.Column<int>(type: "integer", nullable: false),
                    AppliedCount = table.Column<int>(type: "integer", nullable: false),
                    ProposedCount = table.Column<int>(type: "integer", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingCategoryUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedYnabTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingCategoryUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingCategoryUpdates_ProcessedYnabTransactions_ProcessedY~",
                        column: x => x.ProcessedYnabTransactionId,
                        principalTable: "ProcessedYnabTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategorizationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedYnabTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedPayee = table.Column<string>(type: "text", nullable: true),
                    SelectedCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Consistency = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorizationDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategorizationDecisions_ProcessedYnabTransactions_Processed~",
                        column: x => x.ProcessedYnabTransactionId,
                        principalTable: "ProcessedYnabTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategorizationDecisions_ProcessingRuns_ProcessingRunId",
                        column: x => x.ProcessingRunId,
                        principalTable: "ProcessingRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationDecisions_NormalizedPayee_SelectedCategoryId",
                table: "CategorizationDecisions",
                columns: new[] { "NormalizedPayee", "SelectedCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationDecisions_ProcessedYnabTransactionId",
                table: "CategorizationDecisions",
                column: "ProcessedYnabTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationDecisions_ProcessingRunId",
                table: "CategorizationDecisions",
                column: "ProcessingRunId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_NormalizedPayee_IsExplicit",
                table: "MerchantRules",
                columns: new[] { "NormalizedPayee", "IsExplicit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingCategoryUpdates_ProcessedYnabTransactionId_Status",
                table: "PendingCategoryUpdates",
                columns: new[] { "ProcessedYnabTransactionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedYnabTransactions_NormalizedPayee",
                table: "ProcessedYnabTransactions",
                column: "NormalizedPayee");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedYnabTransactions_YnabTransactionId",
                table: "ProcessedYnabTransactions",
                column: "YnabTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategorizationDecisions");

            migrationBuilder.DropTable(
                name: "MerchantRules");

            migrationBuilder.DropTable(
                name: "PendingCategoryUpdates");

            migrationBuilder.DropTable(
                name: "ProcessingRuns");

            migrationBuilder.DropTable(
                name: "ProcessedYnabTransactions");
        }
    }
}
