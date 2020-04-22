using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class RemovePublishingUsingPipelines : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishUsingPipelines",
                table: "Builds");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublishUsingPipelines",
                table: "Builds",
                nullable: false,
                defaultValue: false);
        }
    }
}
