using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class RenameLongestBuildPathDateColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EndDate",
                table: "LongestBuildPaths",
                newName: "ReportDate");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReportDate",
                table: "LongestBuildPaths",
                newName: "EndDate");
        }
    }
}
