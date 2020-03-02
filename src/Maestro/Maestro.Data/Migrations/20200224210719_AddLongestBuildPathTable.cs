using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class AddLongestBuildPathTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LongestBuildPaths",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    ChannelId = table.Column<int>(nullable: false),
                    EndDate = table.Column<DateTimeOffset>(nullable: false),
                    BestCaseTimeInMinutes = table.Column<double>(nullable: false),
                    WorstCaseTimeInMinutes = table.Column<double>(nullable: false),
                    ContributingRepositories = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LongestBuildPaths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LongestBuildPaths_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LongestBuildPaths_ChannelId",
                table: "LongestBuildPaths",
                column: "ChannelId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LongestBuildPaths");
        }
    }
}
