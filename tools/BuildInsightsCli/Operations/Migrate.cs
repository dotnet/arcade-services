// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using BuildInsights.Data;
using CommandLine;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tools.Cli.Core;

namespace BuildInsightsCli.Operations;

[Verb("migrate", HelpText = "Run Entity Framework migrations on the BuildInsights database")]
[Operation<Migrate>]
public class MigrateOptions : Options
{
    [Option("connection-string", Required = true, HelpText = "SQL connection string for the BuildInsights database")]
    public required string SqlConnectionString { get; init; }
}

internal class Migrate(
    MigrateOptions options,
    ILogger<Migrate> logger
) : IOperation
{
    public async Task<int> RunAsync()
    {
        try
        {
            logger.LogInformation("Starting database migration...");

            var accessToken = GetAccessToken();
            var connection = new SqlConnection(options.SqlConnectionString)
            {
                AccessToken = accessToken
            };

            var dbContextOptions = new DbContextOptionsBuilder<BuildInsightsContext>()
                .UseSqlServer(connection)
                .Options;

            await using var dbContext = new BuildInsightsContext(dbContextOptions);

            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            var pending = pendingMigrations.ToList();

            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migrations...", pending.Count);
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migration completed successfully.");
            }
            else
            {
                logger.LogInformation("No pending migrations found.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during database migration");
            return 1;
        }
    }

    private string GetAccessToken()
    {
        var credential = new ChainedTokenCredential(
            new AzureCliCredential(),
            new AzureDeveloperCliCredential());

        var token = credential.GetToken(
            new TokenRequestContext(["https://database.windows.net/.default"]));

        logger.LogInformation(
            "Access token acquired successfully: {TokenAcquired}, ExpiresOn: {ExpiresOn}",
            !string.IsNullOrEmpty(token.Token),
            token.ExpiresOn);

        return token.Token;
    }
}
