using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class MergePrsFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IgnoredChecks",
                table: "Subscriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MergePrs",
                table: "Subscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IgnoredChecks",
                table: "RepositoryBranches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MergePrs",
                table: "RepositoryBranches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IgnoredChecks",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "MergePrs",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "IgnoredChecks",
                table: "RepositoryBranches");

            migrationBuilder.DropColumn(
                name: "MergePrs",
                table: "RepositoryBranches");
        }
    }
}
