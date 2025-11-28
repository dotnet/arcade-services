// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maestro.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNamespace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NamespaceId",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NamespaceId",
                table: "RepositoryBranches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NamespaceId",
                table: "DefaultChannels",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NamespaceId",
                table: "Channels",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Namespaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Namespaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_NamespaceId",
                table: "Subscriptions",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryBranches_NamespaceId",
                table: "RepositoryBranches",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_DefaultChannels_NamespaceId",
                table: "DefaultChannels",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_NamespaceId",
                table: "Channels",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_Name",
                table: "Namespaces",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Channels_Namespaces_NamespaceId",
                table: "Channels",
                column: "NamespaceId",
                principalTable: "Namespaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DefaultChannels_Namespaces_NamespaceId",
                table: "DefaultChannels",
                column: "NamespaceId",
                principalTable: "Namespaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RepositoryBranches_Namespaces_NamespaceId",
                table: "RepositoryBranches",
                column: "NamespaceId",
                principalTable: "Namespaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Namespaces_NamespaceId",
                table: "Subscriptions",
                column: "NamespaceId",
                principalTable: "Namespaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Insert default namespace for the environment
            var connectionstring = BuildAssetRegistryContextFactory.GetConnectionString("BuildAssetRegistry");
            Console.WriteLine($"Connection string: {connectionstring}");
            string namespaceName;
            if (connectionstring.Contains("maestro-int-server"))
            {
                namespaceName = "staging";
            }
            else if (connectionstring.Contains("maestro-prod"))
            {
                namespaceName = "production";
            }
            else
            {
                namespaceName = "local";
            }
            Console.WriteLine($"Using namespace: {namespaceName}");

            migrationBuilder.InsertData(
                table: "Namespaces",
                columns: new[] { "Name" },
                values: new object[] { namespaceName });

            // Update all existing records to use the default namespace
            migrationBuilder.Sql($@"
                DECLARE @MainNamespaceId int = (SELECT Id FROM Namespaces WHERE Name = '{namespaceName}')
                
                UPDATE Subscriptions 
                SET NamespaceId = @MainNamespaceId
                WHERE NamespaceId IS NULL
                
                UPDATE Channels 
                SET NamespaceId = @MainNamespaceId
                WHERE NamespaceId IS NULL
                
                UPDATE DefaultChannels 
                SET NamespaceId = @MainNamespaceId
                WHERE NamespaceId IS NULL
                
                UPDATE RepositoryBranches 
                SET NamespaceId = @MainNamespaceId
                WHERE NamespaceId IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Clear namespace references before dropping foreign keys
            migrationBuilder.Sql(@"
                UPDATE Subscriptions SET NamespaceId = NULL
                UPDATE Channels SET NamespaceId = NULL  
                UPDATE DefaultChannels SET NamespaceId = NULL
                UPDATE RepositoryBranches SET NamespaceId = NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_Channels_Namespaces_NamespaceId",
                table: "Channels");

            migrationBuilder.DropForeignKey(
                name: "FK_DefaultChannels_Namespaces_NamespaceId",
                table: "DefaultChannels");

            migrationBuilder.DropForeignKey(
                name: "FK_RepositoryBranches_Namespaces_NamespaceId",
                table: "RepositoryBranches");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Namespaces_NamespaceId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "Namespaces");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_NamespaceId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryBranches_NamespaceId",
                table: "RepositoryBranches");

            migrationBuilder.DropIndex(
                name: "IX_DefaultChannels_NamespaceId",
                table: "DefaultChannels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_NamespaceId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "RepositoryBranches");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "DefaultChannels");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "Channels");
        }
    }
}
