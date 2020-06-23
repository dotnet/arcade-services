using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class RemoveReleasePipelineEntities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelReleasePipelines");

            migrationBuilder.DropTable(
                name: "ReleasePipelines");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleasePipelines",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Organization = table.Column<string>(nullable: true),
                    PipelineIdentifier = table.Column<int>(nullable: false),
                    Project = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleasePipelines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelReleasePipelines",
                columns: table => new
                {
                    ChannelId = table.Column<int>(nullable: false),
                    ReleasePipelineId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelReleasePipelines", x => new { x.ChannelId, x.ReleasePipelineId });
                    table.ForeignKey(
                        name: "FK_ChannelReleasePipelines_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelReleasePipelines_ReleasePipelines_ReleasePipelineId",
                        column: x => x.ReleasePipelineId,
                        principalTable: "ReleasePipelines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelReleasePipelines_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId");
        }
    }
}
