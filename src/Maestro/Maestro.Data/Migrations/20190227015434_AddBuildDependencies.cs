using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class AddBuildDependencies : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Builds_Builds_DependencyBuildId",
                table: "Builds");

            migrationBuilder.DropIndex(
                name: "IX_Builds_DependencyBuildId",
                table: "Builds");

            migrationBuilder.DropColumn(
                name: "DependencyBuildId",
                table: "Builds");

            migrationBuilder.CreateTable(
                name: "BuildDependencies",
                columns: table => new
                {
                    BuildId = table.Column<int>(nullable: false),
                    DependentBuildId = table.Column<int>(nullable: false),
                    IsProduct = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildDependencies", x => new { x.BuildId, x.DependentBuildId });
                    table.ForeignKey(
                        name: "FK_BuildDependencies_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildDependencies_Builds_DependentBuildId",
                        column: x => x.DependentBuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildDependencies_DependentBuildId",
                table: "BuildDependencies",
                column: "DependentBuildId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildDependencies");

            migrationBuilder.AddColumn<int>(
                name: "DependencyBuildId",
                table: "Builds",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Builds_DependencyBuildId",
                table: "Builds",
                column: "DependencyBuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_Builds_Builds_DependencyBuildId",
                table: "Builds",
                column: "DependencyBuildId",
                principalTable: "Builds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
