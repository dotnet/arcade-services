// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Kusto.Cloud.Platform.Utils;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PackagesInReleaseFeeds = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>>;

namespace ProductConstructionService.FeedCleaner;

public class FeedCleanerJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly IOptions<FeedCleanerOptions> _options;
    private readonly Microsoft.Extensions.Logging.ILogger<FeedCleanerJob> _logger;

    public FeedCleanerJob(
        IServiceProvider serviceProvider,
        IAzureDevOpsClient azureDevOpsClient,
        IOptions<FeedCleanerOptions> options,
        Microsoft.Extensions.Logging.ILogger<FeedCleanerJob> logger)
    {
        _serviceProvider = serviceProvider;
        _azureDevOpsClient = azureDevOpsClient;
        _options = options;
        _logger = logger;
    }

    private FeedCleanerOptions Options => _options.Value;

    public async Task CleanManagedFeedsAsync()
    {
        if (!Options.Enabled)
        {
            _logger.LogInformation("Feed cleaner service is disabled in this environment");
            return;
        }

        foreach (var azdoAccount in Options.AzdoAccounts)
        {
            _logger.LogInformation("Processing feeds for {account}...", azdoAccount);

            List<AzureDevOpsFeed> allFeeds;
            try
            {
                allFeeds = await _azureDevOpsClient.GetFeedsAsync(azdoAccount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get feeds for account {azdoAccount}", azdoAccount);
                continue;
            }

            List<AzureDevOpsFeed> managedFeeds = allFeeds
                .Where(f => Regex.IsMatch(f.Name, FeedConstants.MaestroManagedFeedNamePattern))
                .Shuffle()
                .ToList();

            _logger.LogInformation("Found {totalCount} feeds for {account}. Will process {count} matching feeds",
                allFeeds.Count,
                azdoAccount,
                managedFeeds.Count);

            int feedsCleaned = 0;

            await Parallel.ForEachAsync(
                managedFeeds,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5
                },
                async (AzureDevOpsFeed feed, CancellationToken cancellationToken) =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var feedCleaner = scope.ServiceProvider.GetRequiredService<FeedCleaner>();

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await feedCleaner.CleanFeedAsync(feed);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Failed to clean feed {feed}", feed.Name);
                    }

                    Interlocked.Increment(ref feedsCleaned);
                });

            _logger.Log(
                feedsCleaned != managedFeeds.Count ? LogLevel.Warning : LogLevel.Information,
                "Successfully processed {count}/{totalCount} feeds for {account}",
                feedsCleaned,
                managedFeeds.Count,
                azdoAccount);
        }
    }

    /// <summary>
    /// Construct a nuget feed URL for an Azure DevOps Artifact feed
    /// </summary>
    /// <param name="feedName">Name of the feed</param>
    /// <param name="account">Azure DevOps Account where the feed is hosted</param>
    /// <param name="project">Optional project for the feed.</param>
    /// <returns>Url of the form https://pkgs.dev.azure.com/account/project/_packaging/feedName/nuget/v3/index.json </returns>
    private static string ComputeAzureArtifactsNuGetFeedUrl(string feedName, string account, string project = "")
    {
        string projectSection = string.IsNullOrEmpty(project) ? "" : $"{project}/";
        return $"https://pkgs.dev.azure.com/{account}/{projectSection}_packaging/{feedName}/nuget/v3/index.json";
    }

    /// <summary>
    /// Gets a Mapping of package -> versions for an Azure DevOps feed.
    /// </summary>
    /// <param name="azdoClient">Azure DevOps client.</param>
    /// <param name="account">Azure DevOps account.</param>
    /// <param name="project">Azure DevOps project the feed is hosted in.</param>
    /// <param name="feedName">Name of the feed</param>
    /// <returns>Dictionary where the key is the package name, and the value is a HashSet of the versions of the package in the feed</returns>
    private async Task<Dictionary<string, HashSet<string>>> GetPackageVersionsForFeedAsync(string account, string project, string feedName)
    {
        var packagesWithVersions = new Dictionary<string, HashSet<string>>();
        List<AzureDevOpsPackage> packagesInFeed = await _azureDevOpsClient.GetPackagesForFeedAsync(account, project, feedName);
        foreach (AzureDevOpsPackage package in packagesInFeed)
        {
            packagesWithVersions.Add(package.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            packagesWithVersions[package.Name].UnionWith(package.Versions?.Where(v => !v.IsDeleted).Select(v => v.Version) ?? []);
        }
        return packagesWithVersions;
    }
}
