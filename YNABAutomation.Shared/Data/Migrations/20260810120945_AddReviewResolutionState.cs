using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomation.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewResolutionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MerchantRules_NormalizedPayee_IsExplicit",
                table: "MerchantRules");

            migrationBuilder.AddColumn<string>(
                name: "AccountName",
                table: "ProcessedYnabTransactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "ProcessedYnabTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Memo",
                table: "ProcessedYnabTransactions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "PendingCategoryUpdates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PendingCategoryUpdates",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "MerchantRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsManualObservation",
                table: "CategorizationDecisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_PendingCategoryUpdates_RequestId",
                table: "PendingCategoryUpdates",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_NormalizedPayee_Direction_IsExplicit",
                table: "MerchantRules",
                columns: new[] { "NormalizedPayee", "Direction", "IsExplicit" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingCategoryUpdates_RequestId",
                table: "PendingCategoryUpdates");

            migrationBuilder.DropIndex(
                name: "IX_MerchantRules_NormalizedPayee_Direction_IsExplicit",
                table: "MerchantRules");

            migrationBuilder.DropColumn(
                name: "AccountName",
                table: "ProcessedYnabTransactions");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "ProcessedYnabTransactions");

            migrationBuilder.DropColumn(
                name: "Memo",
                table: "ProcessedYnabTransactions");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "PendingCategoryUpdates");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PendingCategoryUpdates");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "MerchantRules");

            migrationBuilder.DropColumn(
                name: "IsManualObservation",
                table: "CategorizationDecisions");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_NormalizedPayee_IsExplicit",
                table: "MerchantRules",
                columns: new[] { "NormalizedPayee", "IsExplicit" },
                unique: true);
        }
    }
}
