// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    public partial class CodeEnabledSubscriptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SourceEnabled",
                table: "Subscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AssetFilters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Filter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetFilter_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetFilter_SubscriptionId",
                table: "AssetFilters",
                column: "SubscriptionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetFilters");

            migrationBuilder.DropColumn(
                name: "SourceEnabled",
                table: "Subscriptions");
        }
    }
}
