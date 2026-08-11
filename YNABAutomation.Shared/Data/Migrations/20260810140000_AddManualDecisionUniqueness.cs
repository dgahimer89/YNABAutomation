using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomation.Shared.Data.Migrations;

public partial class AddManualDecisionUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_CategorizationDecisions_ProcessedYnabTransactionId_ManualApplied",
            table: "CategorizationDecisions",
            column: "ProcessedYnabTransactionId",
            unique: true,
            filter: "\"IsManualObservation\" = true AND \"Status\" = 'ManualApplied'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CategorizationDecisions_ProcessedYnabTransactionId_ManualApplied",
            table: "CategorizationDecisions");
    }
}
