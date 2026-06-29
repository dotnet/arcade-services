// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRepositoryBranchUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LongestBuildPaths");

            // Disable system versioning before dropping the temporal table
            migrationBuilder.Sql("ALTER TABLE [RepositoryBranchUpdates] SET (SYSTEM_VERSIONING = OFF)");

            migrationBuilder.DropTable(
                name: "RepositoryBranchUpdateHistory");

            migrationBuilder.DropTable(
                name: "RepositoryBranchUpdates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LongestBuildPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    BestCaseTimeInMinutes = table.Column<double>(type: "float", nullable: false),
                    ContributingRepositories = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WorstCaseTimeInMinutes = table.Column<double>(type: "float", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "RepositoryBranchUpdateHistory",
                columns: table => new
                {
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BranchName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "RepositoryBranchUpdates",
                columns: table => new
                {
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryBranchUpdates", x => new { x.RepositoryName, x.BranchName });
                    table.ForeignKey(
                        name: "FK_RepositoryBranchUpdates_RepositoryBranches_RepositoryName_BranchName",
                        columns: x => new { x.RepositoryName, x.BranchName },
                        principalTable: "RepositoryBranches",
                        principalColumns: new[] { "RepositoryName", "BranchName" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RepositoryBranchUpdateHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime");

            migrationBuilder.CreateIndex(
                name: "IX_LongestBuildPaths_ChannelId",
                table: "LongestBuildPaths",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryBranchUpdateHistory_RepositoryName_BranchName_SysEndTime_SysStartTime",
                table: "RepositoryBranchUpdateHistory",
                columns: new[] { "RepositoryName", "BranchName", "SysEndTime", "SysStartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryBranchUpdateHistory_SysEndTime_SysStartTime",
                table: "RepositoryBranchUpdateHistory",
                columns: new[] { "SysEndTime", "SysStartTime" })
                .Annotation("SqlServer:Clustered", true);
        }
    }
}
