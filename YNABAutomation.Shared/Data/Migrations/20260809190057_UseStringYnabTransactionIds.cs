using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomationConsole.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseStringYnabTransactionIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ProcessedYnabTransactions\" " +
                "ALTER COLUMN \"YnabTransactionId\" TYPE text " +
                "USING \"YnabTransactionId\"::text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"ProcessedYnabTransactions\" " +
                "ALTER COLUMN \"YnabTransactionId\" TYPE uuid " +
                "USING \"YnabTransactionId\"::uuid;");
        }
    }
}
