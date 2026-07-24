// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDependencyFlowEventsAndSubscriptionUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DependencyFlowEvents");

            // Disable system versioning before dropping the temporal table
            migrationBuilder.Sql("ALTER TABLE [SubscriptionUpdates] SET (SYSTEM_VERSIONING = OFF)");

            migrationBuilder.DropTable(
                name: "SubscriptionUpdateHistory");

            migrationBuilder.DropTable(
                name: "SubscriptionUpdates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DependencyFlowEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    ChannelId = table.Column<int>(type: "int", nullable: true),
                    Event = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FlowType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceRepository = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TargetRepository = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyFlowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DependencyFlowEvents_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionUpdateHistory",
                columns: table => new
                {
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionUpdates",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_SubscriptionUpdates", x => x.SubscriptionId);
                    table.ForeignKey(
                        name: "FK_SubscriptionUpdates_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionUpdateHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime");

            migrationBuilder.CreateIndex(
                name: "IX_DependencyFlowEvents_BuildId",
                table: "DependencyFlowEvents",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUpdateHistory_SubscriptionId_SysEndTime_SysStartTime",
                table: "SubscriptionUpdateHistory",
                columns: new[] { "SubscriptionId", "SysEndTime", "SysStartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUpdateHistory_SysEndTime_SysStartTime",
                table: "SubscriptionUpdateHistory",
                columns: new[] { "SysEndTime", "SysStartTime" })
                .Annotation("SqlServer:Clustered", true);
        }
    }
}
