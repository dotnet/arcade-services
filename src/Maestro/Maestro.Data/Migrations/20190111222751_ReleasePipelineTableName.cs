using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class ReleasePipelineTableName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReleasePipeline",
                table: "ReleasePipeline");

            migrationBuilder.RenameTable(
                name: "ReleasePipeline",
                newName: "ReleasePipelines");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReleasePipelines",
                table: "ReleasePipelines",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipelines_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId",
                principalTable: "ReleasePipelines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipelines_ReleasePipelineId",
                table: "ChannelReleasePipelines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReleasePipelines",
                table: "ReleasePipelines");

            migrationBuilder.RenameTable(
                name: "ReleasePipelines",
                newName: "ReleasePipeline");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReleasePipeline",
                table: "ReleasePipeline",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId",
                principalTable: "ReleasePipeline",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
