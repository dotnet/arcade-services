using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class AddAzDoBuildInformation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzDoBranch",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AzDoBuildId",
                table: "Builds",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AzDoRepository",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Project",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceBuildDefinitionId",
                table: "Builds",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Account",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzDoBranch",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzDoBuildId",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzDoRepository",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "Project",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "SourceBuildDefinitionId",
                table: "Builds");
        }
    }
}
