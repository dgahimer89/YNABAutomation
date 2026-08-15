using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YNABAutomation.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransferCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    YnabTransactionId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    PayeeName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Cleared = table.Column<bool>(type: "boolean", nullable: false),
                    ExistingYnabTransfer = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchedTransactionId = table.Column<string>(type: "text", nullable: true),
                    PlausibleMatchesJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferCandidates_MatchedTransactionId",
                table: "TransferCandidates",
                column: "MatchedTransactionId",
                unique: true,
                filter: "\"MatchedTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransferCandidates_YnabTransactionId",
                table: "TransferCandidates",
                column: "YnabTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferCandidates");
        }
    }
}
