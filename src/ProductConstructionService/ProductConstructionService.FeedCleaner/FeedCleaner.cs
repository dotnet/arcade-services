// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.FeedCleaner;

public class FeedCleaner
{
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly BuildAssetRegistryContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<FeedCleaner> _logger;

    public FeedCleaner(
        IAzureDevOpsClient azureDevOpsClient,
        BuildAssetRegistryContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<FeedCleaner> logger)
    {
        _azureDevOpsClient = azureDevOpsClient;
        _context = context;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task CleanFeedAsync(AzureDevOpsFeed feed)
    {
        try
        {
            var packages = await _azureDevOpsClient.GetPackagesForFeedAsync(feed.Account, feed.Project?.Name, feed.Name, includeDeleted: false);

            _logger.LogInformation("Cleaning feed {feed} with {count} packages...", feed.Name, packages.Count);

            var updatedCount = 0;

            foreach (var package in packages)
            {
                HashSet<Asset> updatedAssets = await UpdateReleasedVersionsForPackageAsync(feed, package);

                await DeletePackageVersionsFromFeedAsync(feed, updatedAssets);
                updatedCount += updatedAssets.Count;
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

    /// <summary>
    /// Updates the location for assets in the Database when 
    /// a version of an asset is found in the release feeds or in NuGet.org
    /// </summary>
    /// <param name="feed">Feed to examine</param>
    /// <param name="package">Package to search for</param>
    /// <param name="dotnetFeedsPackageMapping">Mapping of packages and their versions in the release feeds</param>
    /// <returns>Collection of versions that were updated for the package</returns>
    private async Task<HashSet<Asset>> UpdateReleasedVersionsForPackageAsync(
        AzureDevOpsFeed feed,
        AzureDevOpsPackage package)
    {
        HashSet<Asset> releasedAssets = new();

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

            if (matchingAsset.Locations.Any(l => l.Location == FeedConstants.NuGetOrgLocation))
            {
                _logger.LogInformation("Package {package}.{version} is already present in a public location.",
                    package.Name,
                    version.Version);
                releasedAssets.Add(matchingAsset);
                continue;
            }

            try
            {
                if (!await IsPackageAvailableInNugetOrgAsync(package.Name, version.Version))
                {
                    _logger.LogInformation("Package {package}.{version} not found in any of the release feeds", package.Name, version);
                    continue;
                }
            }
            catch (HttpRequestException e)
            {
                _logger.LogWarning(e, "Failed to determine if package {package}.{version} is present in NuGet.org",
                    package.Name,
                    version.Version);
                continue;
            }

            releasedAssets.Add(matchingAsset);

            _logger.LogInformation("Found package {package}.{version} in {feed}, adding location to asset",
                package.Name,
                version.Version,
                FeedConstants.NuGetOrgLocation);

            matchingAsset.Locations.Add(new AssetLocation()
            {
                Location = FeedConstants.NuGetOrgLocation,
                Type = LocationType.NugetFeed
            });

            await _context.SaveChangesAsync();
        }

        return releasedAssets;
    }

    /// <summary>
    /// Deletes a version of a package from an Azure DevOps feed
    /// </summary>
    /// <param name="feed">Feed to delete the package from</param>
    /// <param name="packageName">package to delete</param>
    /// <param name="assetsToDelete">Collection of versions to delete</param>
    private async Task DeletePackageVersionsFromFeedAsync(
        AzureDevOpsFeed feed,
        HashSet<Asset> assetsToDelete)
    {
        foreach (Asset asset in assetsToDelete)
        {
            try
            {
                _logger.LogInformation("Deleting package {package}.{version} from feed {feed}",
                    asset.Name, asset.Version, feed.Name);

                await _azureDevOpsClient.DeleteNuGetPackageVersionFromFeedAsync(
                    feed.Account,
                    feed.Project?.Name,
                    feed.Name,
                    asset.Name,
                    asset.Version);

                asset.Locations.Remove(asset.Locations.First(l => l.Location.Contains(feed.Name, StringComparison.OrdinalIgnoreCase)));

                await _context.SaveChangesAsync();
            }
            catch (HttpRequestException e)
            {
                _logger.LogError(e, "There was an error attempting to delete package {package}.{version} from the {feed} feed. Skipping...",
                    asset.Name,
                    asset.Version,
                    feed.Name);
            }
        }
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
