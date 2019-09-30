using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class DependencyFlowEventTableMakeChannelIdNullable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents");

            migrationBuilder.AlterColumn<int>(
                name: "ChannelId",
                table: "DependencyFlowEvents",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AddForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents");

            migrationBuilder.AlterColumn<int>(
                name: "ChannelId",
                table: "DependencyFlowEvents",
                nullable: false,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DependencyFlowEvents_Channels_ChannelId",
                table: "DependencyFlowEvents",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
