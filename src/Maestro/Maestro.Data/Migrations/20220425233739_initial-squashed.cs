// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    public partial class initialsquashed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastUpdated = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Builds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Commit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AzureDevOpsBuildId = table.Column<int>(type: "int", nullable: true),
                    AzureDevOpsBuildDefinitionId = table.Column<int>(type: "int", nullable: true),
                    AzureDevOpsAccount = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AzureDevOpsProject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AzureDevOpsBuildNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AzureDevOpsRepository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AzureDevOpsBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GitHubRepository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GitHubBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateProduced = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Released = table.Column<bool>(type: "bit", nullable: false),
                    Stable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Builds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Classification = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.RepositoryName);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryBranchUpdateHistory",
                columns: table => new
                {
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionUpdateHistory",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPersonalAccessTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApplicationUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPersonalAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserPersonalAccessTokens_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(75)", maxLength: 75, nullable: true),
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    NonShipping = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildDependencies",
                columns: table => new
                {
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    DependentBuildId = table.Column<int>(type: "int", nullable: false),
                    IsProduct = table.Column<bool>(type: "bit", nullable: false),
                    TimeToInclusionInMinutes = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildDependencies", x => new { x.BuildId, x.DependentBuildId });
                    table.ForeignKey(
                        name: "FK_BuildDependencies_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuildDependencies_Builds_DependentBuildId",
                        column: x => x.DependentBuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuildIncoherencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Repository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Commit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BuildId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildIncoherencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildIncoherencies_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DependencyFlowEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceRepository = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TargetRepository = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ChannelId = table.Column<int>(type: "int", nullable: true),
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    Event = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FlowType = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                name: "BuildChannels",
                columns: table => new
                {
                    BuildId = table.Column<int>(type: "int", nullable: false),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    DateTimeAdded = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildChannels", x => new { x.BuildId, x.ChannelId });
                    table.ForeignKey(
                        name: "FK_BuildChannels_Builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "Builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BuildChannels_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefaultChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Repository = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Branch = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefaultChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefaultChannels_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoalTime",
                columns: table => new
                {
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    DefinitionId = table.Column<int>(type: "int", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalTime", x => new { x.DefinitionId, x.ChannelId });
                    table.ForeignKey(
                        name: "FK_GoalTime_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LongestBuildPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BestCaseTimeInMinutes = table.Column<double>(type: "float", nullable: false),
                    WorstCaseTimeInMinutes = table.Column<double>(type: "float", nullable: false),
                    ContributingRepositories = table.Column<string>(type: "nvarchar(max)", nullable: true)
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
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<int>(type: "int", nullable: false),
                    SourceRepository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetRepository = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Policy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LastAppliedBuildId = table.Column<int>(type: "int", nullable: true),
                    PullRequestFailureNotificationTags = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Builds_LastAppliedBuildId",
                        column: x => x.LastAppliedBuildId,
                        principalTable: "Builds",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Subscriptions_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryBranches",
                columns: table => new
                {
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Policy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryBranches", x => new { x.RepositoryName, x.BranchName });
                    table.ForeignKey(
                        name: "FK_RepositoryBranches_Repositories_RepositoryName",
                        column: x => x.RepositoryName,
                        principalTable: "Repositories",
                        principalColumn: "RepositoryName",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetLocations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionUpdates",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:IsTemporal", true)
                        .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                        .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime"),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:IsTemporal", true)
                        .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                        .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime")
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

            migrationBuilder.CreateTable(
                name: "RepositoryBranchUpdates",
                columns: table => new
                {
                    RepositoryName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BranchName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Arguments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SysEndTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:IsTemporal", true)
                        .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                        .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime"),
                    SysStartTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:IsTemporal", true)
                        .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                        .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime")
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
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPersonalAccessTokens_ApplicationUserId_Name",
                table: "AspNetUserPersonalAccessTokens",
                columns: new[] { "ApplicationUserId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssetLocations_AssetId",
                table: "AssetLocations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_BuildId",
                table: "Assets",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Name_Version",
                table: "Assets",
                columns: new[] { "Name", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildChannels_ChannelId",
                table: "BuildChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildDependencies_DependentBuildId",
                table: "BuildDependencies",
                column: "DependentBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildIncoherencies_BuildId",
                table: "BuildIncoherencies",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Name",
                table: "Channels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefaultChannels_ChannelId",
                table: "DefaultChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_DefaultChannels_Repository_Branch_ChannelId",
                table: "DefaultChannels",
                columns: new[] { "Repository", "Branch", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DependencyFlowEvents_BuildId",
                table: "DependencyFlowEvents",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalTime_ChannelId",
                table: "GoalTime",
                column: "ChannelId");

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

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ChannelId",
                table: "Subscriptions",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_LastAppliedBuildId",
                table: "Subscriptions",
                column: "LastAppliedBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUpdateHistory_SubscriptionId_SysEndTime_SysStartTime",
                table: "SubscriptionUpdateHistory",
                columns: new[] { "SubscriptionId", "SysEndTime", "SysStartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionUpdateHistory_SysEndTime_SysStartTime",
                table: "SubscriptionUpdateHistory",
                columns: new[] { "SysEndTime", "SysStartTime" })
                .Annotation("SqlServer:Clustered", true);

            migrationBuilder.Sql(
@"ALTER TABLE dbo.SubscriptionUpdates
SET (SYSTEM_VERSIONING = ON (DATA_CONSISTENCY_CHECK=ON, HISTORY_RETENTION_PERIOD = 6 MONTHS));");

            migrationBuilder.Sql(
@"ALTER TABLE dbo.RepositoryBranchUpdates
SET (SYSTEM_VERSIONING = ON (DATA_CONSISTENCY_CHECK=ON, HISTORY_RETENTION_PERIOD = 6 MONTHS));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserPersonalAccessTokens");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssetLocations");

            migrationBuilder.DropTable(
                name: "BuildChannels");

            migrationBuilder.DropTable(
                name: "BuildDependencies");

            migrationBuilder.DropTable(
                name: "BuildIncoherencies");

            migrationBuilder.DropTable(
                name: "DefaultChannels");

            migrationBuilder.DropTable(
                name: "DependencyFlowEvents");

            migrationBuilder.DropTable(
                name: "GoalTime");

            migrationBuilder.DropTable(
                name: "LongestBuildPaths");

            migrationBuilder.DropTable(
                name: "RepositoryBranchUpdateHistory");

            migrationBuilder.DropTable(
                name: "RepositoryBranchUpdates")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RepositoryBranchUpdateHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime");

            migrationBuilder.DropTable(
                name: "SubscriptionUpdateHistory");

            migrationBuilder.DropTable(
                name: "SubscriptionUpdates")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SubscriptionUpdateHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", null)
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "SysEndTime")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "SysStartTime");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "RepositoryBranches");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropTable(
                name: "Builds");

            migrationBuilder.DropTable(
                name: "Channels");
        }
    }
}
