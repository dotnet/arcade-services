using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class goaltime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalTime",
                columns: table => new
                {
                    ChannelId = table.Column<int>(nullable: false),
                    DefinitionId = table.Column<int>(nullable: false),
                    Minutes = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalTime", x => new { x.DefinitionId, x.ChannelId });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalTime");
        }
    }
}
