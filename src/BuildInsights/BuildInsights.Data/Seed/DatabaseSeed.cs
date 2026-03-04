// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BuildInsights.Data.Seed;

public interface IDatabaseSeed
{
    Task SeedDataAsync(BuildInsightsContext context);
}

public class DatabaseSeed : IDatabaseSeed
{
    public async Task SeedDataAsync(BuildInsightsContext context)
    {
        if (!await context.BuildAnalysisRepositoryConfigurations.AnyAsync())
        {
            context.BuildAnalysisRepositoryConfigurations.AddRange(SeedRepoConfigurations());
        }
        await context.SaveChangesAsync();
    }

    private BuildAnalysisRepositoryConfiguration[] SeedRepoConfigurations()
    {
        return
        [
            new()
            {
                Id = 1,
                Repository = "dotnet/runtime",
                Branch = "main",
                ShouldMergeOnFailureWithKnownIssues = false,
            },
            new()
            {
                Id = 2,
                Repository = "dotnet/roslyn",
                Branch = "main",
                ShouldMergeOnFailureWithKnownIssues = false,
            }
        ];
    }
}
