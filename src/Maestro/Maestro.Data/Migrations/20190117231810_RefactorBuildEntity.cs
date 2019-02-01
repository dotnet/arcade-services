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
                newName: "GitHubRepository");

            migrationBuilder.RenameColumn(
                name: "Branch",
                table: "Builds",
                newName: "GitHubBranch");

            migrationBuilder.RenameColumn(
                name: "BuildNumber",
                table: "Builds",
                newName: "AzureDevOpsBuildNumber");

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsRepository",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsAccount",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsBranch",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "AzureDevOpsBuildDefinitionId",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "AzureDevOpsBuildId",
                table: "Builds",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsProject",
                table: "Builds",
                nullable: true);

            migrationBuilder.Sql("UPDATE Builds SET AzureDevOpsRepository = GitHubRepository WHERE GitHubRepository NOT LIKE '%github%'");
            migrationBuilder.Sql("UPDATE Builds SET AzureDevOpsBranch = GitHubBranch WHERE GitHubRepository NOT LIKE '%github%'");

            migrationBuilder.Sql("UPDATE Builds SET GitHubBranch = NULL WHERE GitHubRepository NOT LIKE '%github%'");
            migrationBuilder.Sql("UPDATE Builds SET GitHubRepository = NULL WHERE GitHubRepository NOT LIKE '%github%'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Builds SET GitHubRepository = AzureDevOpsRepository WHERE GitHubRepository IS NULL");
            migrationBuilder.Sql("UPDATE Builds SET GitHubBranch = AzureDevOpsBranch WHERE GitHubBranch IS NULL");
            
            migrationBuilder.RenameColumn(
                name: "GitHubRepository",
                table: "Builds",
                newName: "Repository");

            migrationBuilder.RenameColumn(
                name: "GitHubBranch",
                table: "Builds",
                newName: "Branch");

            migrationBuilder.RenameColumn(
                name: "AzureDevOpsBuildNumber",
                table: "Builds",
                newName: "BuildNumber");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsAccount",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBranch",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildDefinitionId",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsBuildId",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsRepository",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsProject",
                table: "Builds");
        }
    }
}
