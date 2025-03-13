// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Kusto.Cloud.Platform.Utils;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

            var (packageFeeds, _) = await FetchFeeds(azdoAccount);

            _logger.LogInformation("Processing {feedCount} package feeds...", packageFeeds.Count);

            int feedsCleaned = await ProcessFeedsInParallelAsync(packageFeeds, async (scope, feed) =>
            {
                var feedCleaner = scope.ServiceProvider.GetRequiredService<FeedCleaner>();
                await feedCleaner.CleanFeedAsync(feed);
                await Task.CompletedTask;
            });

            _logger.Log(
                feedsCleaned != packageFeeds.Count ? LogLevel.Warning : LogLevel.Information,
                "Successfully processed {count}/{totalCount} package feeds for {account}",
                feedsCleaned,
                packageFeeds.Count,
                azdoAccount);

            // We refresh the feed information so that symbol feeds can be cleaned based on up-to-date package feeds
            (packageFeeds, List<AzureDevOpsFeed> symbolFeeds) = await FetchFeeds(azdoAccount);

            _logger.LogInformation("Processing {feedCount} symbol feeds...", symbolFeeds.Count);

            feedsCleaned = await ProcessFeedsInParallelAsync(symbolFeeds, async (scope, feed) =>
            {
                var feedCleaner = scope.ServiceProvider.GetRequiredService<FeedCleaner>();
                await feedCleaner.CleanSymbolFeedAsync(packageFeeds, feed);
            });

            _logger.Log(
                feedsCleaned != symbolFeeds.Count ? LogLevel.Warning : LogLevel.Information,
                "Successfully processed {count}/{totalCount} symbol feeds for {account}",
                feedsCleaned,
                symbolFeeds.Count,
                azdoAccount);
        }
    }

    private async Task<int> ProcessFeedsInParallelAsync(List<AzureDevOpsFeed> feeds, Func<IServiceScope, AzureDevOpsFeed, Task> feedProcessor)
    {
        int feedsProcessed = 0;
        await Parallel.ForEachAsync(
            feeds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 5
            },
            async (AzureDevOpsFeed feed, CancellationToken cancellationToken) =>
            {
                using var scope = _serviceProvider.CreateScope();

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await feedProcessor(scope, feed);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to process feed {feed}", feed.Name);
                }

                Interlocked.Increment(ref feedsProcessed);
            });

        return feedsProcessed;
    }

    private async Task<(List<AzureDevOpsFeed> PackageFeeds, List<AzureDevOpsFeed> SymbolFeeds)> FetchFeeds(string azdoAccount)
    {
        List<AzureDevOpsFeed> allFeeds;
        try
        {
            allFeeds = await _azureDevOpsClient.GetFeedsAsync(azdoAccount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get feeds for account {account}", azdoAccount);
            return ([], []);
        }

        List<AzureDevOpsFeed> packageFeeds = allFeeds
            .Where(f => FeedConstants.MaestroManagedFeedNamePattern.IsMatch(f.Name))
            .Shuffle()
            .ToList();

        List<AzureDevOpsFeed> symbolFeeds = allFeeds
            .Where(f => FeedConstants.MaestroManagedSymbolFeedNamePattern.IsMatch(f.Name))
            .Shuffle()
            .ToList();

        _logger.LogInformation("Found {totalCount} ({packageFeedCount} package and {symbolFeedCount} symbol) feeds for account {account}.",
            allFeeds.Count,
            packageFeeds.Count,
            symbolFeeds.Count,
            azdoAccount);

        return (packageFeeds, symbolFeeds);
    }
}
