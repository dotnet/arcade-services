using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class RefactorBuildEntity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Repository",
                table: "Builds",
                newName: "GitHubBuildInfo_Repository");

            migrationBuilder.RenameColumn(
                name: "Commit",
                table: "Builds",
                newName: "GitHubBuildInfo_Commit");

            migrationBuilder.RenameColumn(
                name: "BuildNumber",
                table: "Builds",
                newName: "AzureDevOpsBuildInfo_BuildNumber");

            migrationBuilder.RenameColumn(
                name: "Branch",
                table: "Builds",
                newName: "GitHubBuildInfo_Branch");

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsBuildInfo_Account",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsBuildInfo_Branch",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AzureDevOpsBuildInfo_BuildDefinitionId",
                table: "Builds",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsBuildInfo_Project",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsBuildInfo_Repository",
                table: "Builds",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildInfo_Account",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildInfo_Branch",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildInfo_BuildDefinitionId",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildInfo_Project",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildInfo_Repository",
                table: "Builds");

            migrationBuilder.RenameColumn(
                name: "GitHubBuildInfo_Repository",
                table: "Builds",
                newName: "Repository");

            migrationBuilder.RenameColumn(
                name: "GitHubBuildInfo_Commit",
                table: "Builds",
                newName: "Commit");

            migrationBuilder.RenameColumn(
                name: "GitHubBuildInfo_Branch",
                table: "Builds",
                newName: "Branch");

            migrationBuilder.RenameColumn(
                name: "AzureDevOpsBuildInfo_BuildNumber",
                table: "Builds",
                newName: "BuildNumber");
        }
    }
}
