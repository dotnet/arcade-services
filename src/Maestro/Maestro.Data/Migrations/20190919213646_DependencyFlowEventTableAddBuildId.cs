using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class DependencyFlowEventTableAddBuildId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildId",
                table: "DependencyFlowEvents",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_DependencyFlowEvents_BuildId",
                table: "DependencyFlowEvents",
                column: "BuildId");

            migrationBuilder.AddForeignKey(
                name: "FK_DependencyFlowEvents_Builds_BuildId",
                table: "DependencyFlowEvents",
                column: "BuildId",
                principalTable: "Builds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DependencyFlowEvents_Builds_BuildId",
                table: "DependencyFlowEvents");

            migrationBuilder.DropIndex(
                name: "IX_DependencyFlowEvents_BuildId",
                table: "DependencyFlowEvents");

            migrationBuilder.DropColumn(
                name: "BuildId",
                table: "DependencyFlowEvents");
        }
    }
}
