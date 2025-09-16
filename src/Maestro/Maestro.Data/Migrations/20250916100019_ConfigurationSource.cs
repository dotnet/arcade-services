using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    public partial class ConfigurationSource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfigurationSourceId",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfigurationSourceId",
                table: "Channels",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfigurationSourceId",
                table: "DefaultChannels",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfigurationSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uri = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ConfigurationSourceId",
                table: "Subscriptions",
                column: "ConfigurationSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_ConfigurationSourceId",
                table: "Channels",
                column: "ConfigurationSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DefaultChannels_ConfigurationSourceId",
                table: "DefaultChannels",
                column: "ConfigurationSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationSources_Uri_Branch",
                table: "ConfigurationSources",
                columns: new[] { "Uri", "Branch" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DefaultChannels_ConfigurationSources_ConfigurationSourceId",
                table: "DefaultChannels",
                column: "ConfigurationSourceId",
                principalTable: "ConfigurationSources",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_ConfigurationSources_ConfigurationSourceId",
                table: "Channels",
                column: "ConfigurationSourceId",
                principalTable: "ConfigurationSources",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_ConfigurationSources_ConfigurationSourceId",
                table: "Subscriptions",
                column: "ConfigurationSourceId",
                principalTable: "ConfigurationSources",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DefaultChannels_ConfigurationSources_ConfigurationSourceId",
                table: "DefaultChannels");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_ConfigurationSources_ConfigurationSourceId",
                table: "Channels");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_ConfigurationSources_ConfigurationSourceId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "ConfigurationSources");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_ConfigurationSourceId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Channels_ConfigurationSourceId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_DefaultChannels_ConfigurationSourceId",
                table: "DefaultChannels");

            migrationBuilder.DropColumn(
                name: "ConfigurationSourceId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ConfigurationSourceId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ConfigurationSourceId",
                table: "DefaultChannels");
        }
    }
}
