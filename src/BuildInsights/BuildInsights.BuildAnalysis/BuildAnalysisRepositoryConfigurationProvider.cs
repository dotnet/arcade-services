// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Data;
using BuildInsights.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BuildInsights.BuildAnalysis;

public interface IBuildAnalysisRepositoryConfigurationService
{
    Task<BuildAnalysisRepositoryConfiguration> GetRepositoryConfiguration(
        string repository,
        string branch,
        CancellationToken cancellationToken);
}

public class BuildAnalysisRepositoryConfigurationProvider : IBuildAnalysisRepositoryConfigurationService
{
    private readonly BuildInsightsContext _context;
    private readonly ILogger<BuildAnalysisRepositoryConfigurationProvider> _logger;
    private const string BranchWildCard = "*";

    public BuildAnalysisRepositoryConfigurationProvider(
        BuildInsightsContext context,
        ILogger<BuildAnalysisRepositoryConfigurationProvider> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<BuildAnalysisRepositoryConfiguration> GetRepositoryConfiguration(string repository, string branch, CancellationToken cancellationToken)
    {
        string normalizedRepository = NormalizeString(repository);
        string normalizedBranch = NormalizeString(branch);

        List<BuildAnalysisRepositoryConfiguration> repoConfigs = await _context.BuildAnalysisRepositoryConfigurations
            .Where(c => c.Repository == normalizedRepository && (c.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase) || c.Branch == BranchWildCard))
            .ToListAsync(cancellationToken);

        // We only expect a single entry per repo for a specific branch.
        // If there are multiple, log as this is an unexpected scenario.
        if (repoConfigs.Count(c => c.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            _logger.LogWarning("Found multiple Build Analysis configurations for repo {repo} at branch {branch}.", repository, branch);
        }

        // Attempt to return the branch specific configuration if it exists,
        // or the repo global configuration using the wildcard if it doesn't
        return repoConfigs.FirstOrDefault(c => c.Branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase))
            ?? repoConfigs.FirstOrDefault(c => c.Branch == BranchWildCard);
    }

    private static string NormalizeString(string value) => value?.Replace('/', '-') ?? string.Empty;
}
