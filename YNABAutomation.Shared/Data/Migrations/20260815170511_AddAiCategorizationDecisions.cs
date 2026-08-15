using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomation.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCategorizationDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCategorizationDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessedYnabTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposedCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedCategoryName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AlternativeCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlternativeCategoryName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequiresReview = table.Column<bool>(type: "boolean", nullable: false),
                    MetAutoApplyThreshold = table.Column<bool>(type: "boolean", nullable: false),
                    WasAutoApplied = table.Column<bool>(type: "boolean", nullable: false),
                    FinalCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCategorizationDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCategorizationDecisions_ProcessedYnabTransactions_Process~",
                        column: x => x.ProcessedYnabTransactionId,
                        principalTable: "ProcessedYnabTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCategorizationDecisions_ProcessedYnabTransactionId_Create~",
                table: "AiCategorizationDecisions",
                columns: new[] { "ProcessedYnabTransactionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCategorizationDecisions");
        }
    }
}
