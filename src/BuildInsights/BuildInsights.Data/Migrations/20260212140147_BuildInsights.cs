// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildInsights.Data.Migrations;

/// <inheritdoc />
public partial class BuildInsights : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BuildAnalysisEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PipelineName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                BuildId = table.Column<int>(type: "int", nullable: false),
                Repository = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Project = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                IsRepositorySupported = table.Column<bool>(type: "bit", nullable: false),
                AnalysisTimestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BuildAnalysisEvents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BuildAnalysisRepositoryConfigurations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Repository = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                Branch = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ShouldMergeOnFailureWithKnownIssues = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BuildAnalysisRepositoryConfigurations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BuildProcessingStatusEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Repository = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                BuildId = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BuildProcessingStatusEvents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "KnownIssueAnalysis",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                IssueId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                BuildId = table.Column<int>(type: "int", nullable: false),
                ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnownIssueAnalysis", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "KnownIssueErrors",
            columns: table => new
            {
                Repository = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IssueId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnownIssueErrors", x => new { x.Repository, x.IssueId });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BuildAnalysisEvents");

        migrationBuilder.DropTable(
            name: "BuildAnalysisRepositoryConfigurations");

        migrationBuilder.DropTable(
            name: "BuildProcessingStatusEvents");

        migrationBuilder.DropTable(
            name: "KnownIssueAnalysis");

        migrationBuilder.DropTable(
            name: "KnownIssueErrors");
    }
}
