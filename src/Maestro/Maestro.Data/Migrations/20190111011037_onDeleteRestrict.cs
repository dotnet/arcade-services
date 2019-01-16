using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class onDeleteRestrict : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_Channels_ChannelId",
                table: "ChannelReleasePipelines");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_Channels_ChannelId",
                table: "ChannelReleasePipelines",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId",
                principalTable: "ReleasePipeline",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_Channels_ChannelId",
                table: "ChannelReleasePipelines");

            migrationBuilder.DropForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_Channels_ChannelId",
                table: "ChannelReleasePipelines",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId",
                principalTable: "ReleasePipeline",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
