using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class AddReleasePipelineConcept : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleasePipeline",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Organization = table.Column<string>(nullable: true),
                    Project = table.Column<string>(nullable: true),
                    PipelineIdentifier = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleasePipeline", x => x.Id);
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
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelReleasePipelines_ReleasePipeline_ReleasePipelineId",
                        column: x => x.ReleasePipelineId,
                        principalTable: "ReleasePipeline",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelReleasePipelines_ReleasePipelineId",
                table: "ChannelReleasePipelines",
                column: "ReleasePipelineId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelReleasePipelines");

            migrationBuilder.DropTable(
                name: "ReleasePipeline");
        }
    }
}
