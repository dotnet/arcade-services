// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using PackagesInReleaseFeeds = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>>;

namespace ProductConstructionService.FeedCleaner;

public class FeedCleaner : IDisposable
{
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly BuildAssetRegistryContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FeedCleaner> _logger;

    public FeedCleaner(
        IAzureDevOpsClient azureDevOpsClient,
        BuildAssetRegistryContext context,
        ILogger<FeedCleaner> logger)
    {
        _azureDevOpsClient = azureDevOpsClient;
        _context = context;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler() { CheckCertificateRevocationList = true });
    }

    public async Task CleanFeedAsync(AzureDevOpsFeed feed, PackagesInReleaseFeeds packagesInReleaseFeeds)
    {
        try
        {
            var packages = await _azureDevOpsClient.GetPackagesForFeedAsync(feed.Account, feed.Project?.Name, feed.Name, includeDeleted: false);

            _logger.LogInformation("Cleaning feed {feed} with {count} packages...", feed.Name, packages.Count);

            var updatedCount = 0;

            foreach (var package in packages)
            {
                HashSet<string> updatedVersions = await UpdateReleasedVersionsForPackageAsync(feed, package, packagesInReleaseFeeds);

                await DeletePackageVersionsFromFeedAsync(feed, package.Name, updatedVersions);
                updatedCount += updatedVersions.Count;
            }

            _logger.LogInformation("Feed {feed} cleaning finished with {count}/{totalCount} updated packages", feed.Name, updatedCount, packages.Count);

            // TODO https://github.com/dotnet/core-eng/issues/9366: Do not remove feeds because it can break branches that still depend on those
            // packages = await _azureDevOpsClient.GetPackagesForFeedAsync(feed.Account, feed.Project?.Name, feed.Name);
            // if (!packages.Any(packages => packages.Versions.Any(v => !v.IsDeleted)))
            // {
            //     _logger.LogInformation("Feed {feed} has no packages left, deleting the feed", feed.Name);
            //     await _azureDevOpsClient.DeleteFeedAsync(feed.Account, feed.Project?.Name, feed.Name);
            // }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something failed while trying to update the released packages in feed {feed}", feed.Name);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _context.Dispose();
    }

    /// <summary>
    /// Updates the location for assets in the Database when 
    /// a version of an asset is found in the release feeds or in NuGet.org
    /// </summary>
    /// <param name="feed">Feed to examine</param>
    /// <param name="package">Package to search for</param>
    /// <param name="dotnetFeedsPackageMapping">Mapping of packages and their versions in the release feeds</param>
    /// <returns>Collection of versions that were updated for the package</returns>
    private async Task<HashSet<string>> UpdateReleasedVersionsForPackageAsync(
        AzureDevOpsFeed feed,
        AzureDevOpsPackage package,
        PackagesInReleaseFeeds dotnetFeedsPackageMapping)
    {
        var releasedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in package.Versions)
        {
            var matchingAssets = _context.Assets
                .Include(a => a.Locations)
                .Where(a => a.Name == package.Name &&
                            a.Version == version.Version)
                .AsEnumerable();

            var matchingAsset = matchingAssets.FirstOrDefault(
                a => a.Locations.Any(l => l.Location.Contains(feed.Name)));

            if (matchingAsset == null)
            {
                _logger.LogInformation("Asset {package}.{version} from feed {feed} not found in BAR. Skipping...",
                    package.Name,
                    version.Version,
                    feed.Name);
                continue;
            }

            if (matchingAsset.Locations.Any(l => l.Location == FeedConstants.NuGetOrgLocation ||
                                                 dotnetFeedsPackageMapping.Any(f => l.Location == f.Key)))
            {
                _logger.LogInformation("Package {package}.{version} is already present in a public location.",
                    package.Name,
                    version.Version);
                releasedVersions.Add(version.Version);
                continue;
            }

            List<string> feedsWherePackageIsAvailable = GetReleaseFeedsWherePackageIsAvailable(
                package.Name,
                version.Version,
                dotnetFeedsPackageMapping);

            try
            {
                if (await IsPackageAvailableInNugetOrgAsync(package.Name, version.Version))
                {
                    feedsWherePackageIsAvailable.Add(FeedConstants.NuGetOrgLocation);
                }
            }
            catch (HttpRequestException e)
            {
                _logger.LogWarning(e, "Failed to determine if package {package}.{version} is present in NuGet.org",
                    package.Name,
                    version.Version);
            }

            if (feedsWherePackageIsAvailable.Count <= 0)
            {
                _logger.LogInformation("Package {package}.{version} not found in any of the release feeds", package.Name, version);
                continue;
            }

            releasedVersions.Add(version.Version);
            foreach (string feedToAdd in feedsWherePackageIsAvailable)
            {
                _logger.LogInformation("Found package {package}.{version} in {feed}, adding location to asset",
                    package.Name,
                    version.Version,
                    feedToAdd);

                matchingAsset.Locations.Add(new AssetLocation()
                {
                    Location = feedToAdd,
                    Type = LocationType.NugetFeed
                });

                await _context.SaveChangesAsync();
            }
        }

        return releasedVersions;
    }

    /// <summary>
    /// Deletes a version of a package from an Azure DevOps feed
    /// </summary>
    /// <param name="feed">Feed to delete the package from</param>
    /// <param name="packageName">package to delete</param>
    /// <param name="versionsToDelete">Collection of versions to delete</param>
    private async Task DeletePackageVersionsFromFeedAsync(
        AzureDevOpsFeed feed,
        string packageName,
        HashSet<string> versionsToDelete)
    {
        foreach (string version in versionsToDelete)
        {
            try
            {
                _logger.LogInformation("Deleting package {package}.{version} from feed {feed}",
                    packageName, version, feed.Name);

                await _azureDevOpsClient.DeleteNuGetPackageVersionFromFeedAsync(
                    feed.Account,
                    feed.Project?.Name,
                    feed.Name,
                    packageName,
                    version);
            }
            catch (HttpRequestException e)
            {
                _logger.LogError(e, "There was an error attempting to delete package {package}.{version} from the {feed} feed. Skipping...",
                    packageName,
                    version,
                    feed.Name);
            }
        }
    }

    /// <summary>
    /// Gets a list of feeds where a given package is available
    /// </summary>
    /// <param name="name">Package to search for</param>
    /// <param name="version">Version to search for</param>
    /// <param name="packageMappings">Feeds to search</param>
    /// <returns>List of feeds in the package mappings where the provided package and version are available</returns>
    private static List<string> GetReleaseFeedsWherePackageIsAvailable(
        string name,
        string version,
        PackagesInReleaseFeeds packageMappings)
    {
        List<string> feeds = [];
        foreach ((string feedName, Dictionary<string, HashSet<string>> packages) in packageMappings)
        {
            if (packages.TryGetValue(name, out HashSet<string>? versions) && versions.Contains(version))
            {
                feeds.Add(feedName);
            }
        }

        return feeds;
    }

    /// <summary>
    /// Checks whether a package is available in NuGet.org
    /// by making a HEAD request to the package contents URI.
    /// </summary>
    /// <param name="name">Package to search for</param>
    /// <param name="version">Version to search for</param>
    /// <returns>True if the package is available in NuGet.org, false if not</returns>
    private async Task<bool> IsPackageAvailableInNugetOrgAsync(string name, string version)
    {
        string packageContentsUri = $"{FeedConstants.NuGetOrgPackageBaseUrl}{name.ToLower()}/{version}/{name.ToLower()}.{version}.nupkg";
        try
        {
            using HttpRequestMessage headRequest = new(HttpMethod.Head, new Uri(packageContentsUri));
            using HttpResponseMessage response = await _httpClient.SendAsync(headRequest);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Found {package}.{version} in nuget.org URI: {uri}", name, version, packageContentsUri);
            return true;
        }
        catch (HttpRequestException e) when (e.Message.Contains(((int)HttpStatusCode.NotFound).ToString()))
        {
            _logger.LogDebug("Package {package}.{version} not found on nuget.org", name, version);
            return false;
        }
    }
}
