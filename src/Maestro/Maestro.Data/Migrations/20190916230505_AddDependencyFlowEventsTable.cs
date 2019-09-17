using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Maestro.Data.Migrations
{
    public partial class AddDependencyFlowEventsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DependencyFlowEvents",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    SourceRepository = table.Column<string>(maxLength: 450, nullable: true),
                    TargetRepository = table.Column<string>(maxLength: 450, nullable: true),
                    ChannelId = table.Column<int>(nullable: false),
                    Event = table.Column<string>(nullable: true),
                    Reason = table.Column<string>(nullable: true),
                    FlowType = table.Column<string>(nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyFlowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DependencyFlowEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DependencyFlowEvents_ChannelId",
                table: "DependencyFlowEvents",
                column: "ChannelId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DependencyFlowEvents");
        }
    }
}
