using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomation.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorizationDecisionDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "CategorizationDecisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationDecisions_NormalizedPayee_Direction_SelectedCategoryId",
                table: "CategorizationDecisions",
                columns: new[] { "NormalizedPayee", "Direction", "SelectedCategoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CategorizationDecisions_NormalizedPayee_Direction_SelectedCategoryId",
                table: "CategorizationDecisions");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "CategorizationDecisions");
        }
    }
}
