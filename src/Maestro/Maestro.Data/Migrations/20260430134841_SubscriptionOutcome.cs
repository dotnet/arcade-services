using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionOutcomes",
                columns: table => new
                {
                    OperationId = table.Column<string>(type: "nchar(32)", fixedLength: true, maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OutcomeMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutcomeType = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionOutcomes", x => x.OperationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOutcomes_BuildId",
                table: "SubscriptionOutcomes",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOutcomes_Date",
                table: "SubscriptionOutcomes",
                column: "Date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOutcomes_SubscriptionId_Date",
                table: "SubscriptionOutcomes",
                columns: new[] { "SubscriptionId", "Date" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionOutcomes");
        }
    }
}
