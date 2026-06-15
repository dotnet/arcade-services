using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionOutcomePrUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrUrl",
                table: "SubscriptionOutcomes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrUrl",
                table: "SubscriptionOutcomes");
        }
    }
}
