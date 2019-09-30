using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class DependencyFlowEventTableRemoveChannel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents");

            migrationBuilder.DropIndex(
                name: "IX_DependencyFlowEvents_ChannelId",
                table: "DependencyFlowEvents");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DependencyFlowEvents_ChannelId",
                table: "DependencyFlowEvents",
                column: "ChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
