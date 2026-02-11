// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BuildInsights.Data;

public class BuildInsightsContextFactory : IDesignTimeDbContextFactory<BuildInsightsContext>
{
    public BuildInsightsContext CreateDbContext(string[] args)
    {
        var connectionString = GetConnectionString("BuildInsights");

        DbContextOptions options = new DbContextOptionsBuilder()
            .UseSqlServer(connectionString, opts =>
            {
                opts.CommandTimeout(30 * 60);
                opts.EnableRetryOnFailure();
            })
            .Options;

        return new BuildInsightsContext(options);
    }

    public static string GetConnectionString(string databaseName)
    {
        var connectionString = $@"Data Source=localhost\SQLEXPRESS;Initial Catalog={databaseName};Integrated Security=true;Encrypt=false"; // CodeQL [SM03452] This 'connection string' is only for the local SQLExpress instance and has no credentials, Encrypt=false for .NET 8+ compatibility
        var envVarConnectionString = Environment.GetEnvironmentVariable("BUILD_INSIGHTS_DB_CONNECTION_STRING");
        if (string.IsNullOrEmpty(envVarConnectionString))
        {
            return connectionString;
        }
        else
        {
            return envVarConnectionString;
        }
    }
}

public class BuildInsightsContext : DbContext
{
    public DbSet<KnownIssueError> KnownIssueErrors { get; set; }

    public BuildInsightsContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KnownIssueError>()
            .HasKey(e => new { e.Repository, e.IssueId });

        modelBuilder.Entity<BuildProcessingStatusEvent>()
            .Property(e => e.Status)
            .HasConversion(
                status => status.Value,                            // To database
                value => BuildProcessingStatus.FromString(value)); // From database
    }
}
