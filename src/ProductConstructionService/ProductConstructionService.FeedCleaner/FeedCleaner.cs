// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.RegularExpressions;
using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProductConstructionService.FeedCleaner;

public class FeedCleaner
{
    private BuildAssetRegistryContext _context;
    private readonly HttpClient _httpClient;
    private readonly IAzureDevOpsClient _azureDevOpsClient;
    private readonly IOptions<FeedCleanerOptions> _options;
    private ILogger<FeedCleaner> _logger;

    public FeedCleaner(
        BuildAssetRegistryContext context,
        IAzureDevOpsClient azureDevOpsClient,
        IOptions<FeedCleanerOptions> options,
        ILogger<FeedCleaner> logger)
    {
        _context = context;
        _azureDevOpsClient = azureDevOpsClient;
        _options = options;
        _logger = logger;
        _httpClient = new HttpClient(new HttpClientHandler() { CheckCertificateRevocationList = true });
    }

    private FeedCleanerOptions Options => _options.Value;

    public async Task CleanManagedFeedsAsync()
    {
        if (!Options.Enabled)
        {
            _logger.LogInformation("Feed cleaner service is disabled in this environment");
            return;
        }

        Dictionary<string, Dictionary<string, HashSet<string>>> packagesInReleaseFeeds =
            await GetPackagesForReleaseFeedsAsync();

        foreach (var azdoAccount in Options.GetAzdoAccounts())
        {
            List<AzureDevOpsFeed> allFeeds;
            try
            {
                allFeeds = await _azureDevOpsClient.GetFeedsAsync(azdoAccount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get feeds for account {account}", azdoAccount);
                continue;
            }
            IEnumerable<AzureDevOpsFeed> managedFeeds = allFeeds.Where(f => Regex.IsMatch(f.Name, FeedConstants.MaestroManagedFeedNamePattern));

            foreach (var feed in managedFeeds)
            {
                try
                {
                    await PopulatePackagesForFeedAsync(feed);
                    foreach (var package in feed.Packages)
                    {
                        HashSet<string> updatedVersions =
                            await UpdateReleasedVersionsForPackageAsync(feed, package, packagesInReleaseFeeds);

                        await DeletePackageVersionsFromFeedAsync(feed, package.Name, updatedVersions);
                    }
                    // We may have deleted all packages in the previous operation, if so, we should delete the feed,
                    // refresh the packages in the feed to check this.
                    await PopulatePackagesForFeedAsync(feed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Something failed while trying to update the released packages in feed {feed}", feed.Name);
                }
            }
        }    
    }

    /// <summary>
    /// Get a mapping of feed -> (package, versions) for the release feeds so it
    /// can be easily queried whether a version of a package is in a feed.
    /// </summary>
    /// <returns>Mapping of packages to versions for the release feeds.</returns>
    private async Task<Dictionary<string, Dictionary<string, HashSet<string>>>> GetPackagesForReleaseFeedsAsync()
    {
        var packagesWithVersionsInReleaseFeeds = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        IEnumerable<ReleasePackageFeed> dotnetManagedFeeds = Options.ReleasePackageFeeds;
        foreach ((string account, string project, string feedName) in dotnetManagedFeeds)
        {
            string readableFeedURL = ComputeAzureArtifactsNuGetFeedUrl(feedName, account, project);

            packagesWithVersionsInReleaseFeeds[readableFeedURL] = await GetPackageVersionsForFeedAsync(account, project, feedName);
        }
        return packagesWithVersionsInReleaseFeeds;
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
        Dictionary<string, Dictionary<string, HashSet<string>>> dotnetFeedsPackageMapping)
    {
        var releasedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var version in package.Versions)
        {
            var matchingAssets = _context.Assets
                .Include(a => a.Locations)
                .Where(a => a.Name == package.Name &&
                            a.Version == version.Version).AsEnumerable();

            var matchingAsset = matchingAssets.FirstOrDefault(
                a => a.Locations.Any(l => l.Location.Contains(feed.Name)));

            if (matchingAsset == null)
            {
                _logger.LogError($"Unable to find asset {package.Name}.{version.Version} in feed {feed.Name} in BAR. " +
                                $"Unable to determine if it was released or update its locations.");
                continue;
            }
            else
            {
                if (matchingAsset.Locations.Any(l => l.Location == FeedConstants.NuGetOrgLocation ||
                                                     dotnetFeedsPackageMapping.Any(f => l.Location == f.Key)))
                {
                    _logger.LogInformation($"Package {package.Name}.{version.Version} is already present in a public location.");
                    releasedVersions.Add(version.Version);
                }
                else
                {
                    List<string> feedsWherePackageIsAvailable = GetReleaseFeedsWherePackageIsAvailable(package.Name,
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
                        _logger.LogInformation(e, $"Failed to determine if package {package.Name}.{version.Version} is present in NuGet.org");
                    }


                    if (feedsWherePackageIsAvailable.Count > 0)
                    {
                        releasedVersions.Add(version.Version);
                        foreach (string feedToAdd in feedsWherePackageIsAvailable)
                        {
                            _logger.LogInformation($"Found package {package.Name}.{version.Version} in " +
                                                  $"{feedToAdd}, adding location to asset.");

                            // TODO (https://github.com/dotnet/arcade-services/issues/3808) Don't actually do anything in BAR before we migrate fully to PCS
                            /*matchingAsset.Locations.Add(new AssetLocation()
                            {
                                Location = feedToAdd,
                                Type = LocationType.NugetFeed
                            });
                            await _context.SaveChangesAsync();*/
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Unable to find {package.Name}.{version} in any of the release feeds");
                    }
                }
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
    /// <returns></returns>
    private async Task DeletePackageVersionsFromFeedAsync(AzureDevOpsFeed feed, string packageName, HashSet<string> versionsToDelete)
    {
        foreach (string version in versionsToDelete)
        {
            try
            {
                _logger.LogInformation($"Deleting package {packageName}.{version} from feed {feed.Name}");

                await _azureDevOpsClient.DeleteNuGetPackageVersionFromFeedAsync(feed.Account,
                    feed.Project?.Name,
                    feed.Name,
                    packageName,
                    version);
            }
            catch (HttpRequestException e)
            {
                _logger.LogError(e, $"There was an error attempting to delete package {packageName}.{version} from the {feed.Name} feed. Skipping...");
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
    private List<string> GetReleaseFeedsWherePackageIsAvailable(
        string name,
        string version,
        Dictionary<string, Dictionary<string, HashSet<string>>> packageMappings)
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
            _logger.LogInformation($"Found {name}.{version} in nuget.org URI: {packageContentsUri}");
            return true;
        }
        catch (HttpRequestException e) when (e.Message.Contains(((int)HttpStatusCode.NotFound).ToString()))
        {
            _logger.LogInformation($"Unable to find {name}.{version} in nuget.org URI: {packageContentsUri}");
            return false;
        }
    }

    /// <summary>
    /// Populates the packages and versions for a given feed
    /// </summary>
    /// <param name="feed">Feed to populate</param>
    /// <returns></returns>
    private async Task PopulatePackagesForFeedAsync(AzureDevOpsFeed feed)
    {
        feed.Packages = await _azureDevOpsClient.GetPackagesForFeedAsync(feed.Account, feed.Project?.Name, feed.Name);
    }
}
