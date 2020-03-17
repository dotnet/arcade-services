// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.AzureDevOps;
using Maestro.Contracts;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FeedCleanerService
{

    /// <summary>
    ///     An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    public sealed class FeedCleanerService : IFeedCleanerService, IServiceImplementation
    {
        public FeedCleanerService(
            ILogger<FeedCleanerService> logger,
            BuildAssetRegistryContext context,
            IOptions<FeedCleanerOptions> options)
        {
            Logger = logger;
            Context = context;
            _httpClient = new HttpClient();
            _options = options;
            AzureDevOpsClients = new Dictionary<string, IAzureDevOpsClient>();
            foreach (string account in _options.Value.AzdoAccounts)
            {
                AzureDevOpsClients.Add(account, GetAzureDevOpsClientForAccountAsync(account));
            }
        }

        public ILogger<FeedCleanerService> Logger { get; }
        public BuildAssetRegistryContext Context { get; }

        public FeedCleanerOptions Options => _options.Value;

        public Dictionary<string, IAzureDevOpsClient> AzureDevOpsClients { get; set; }

        private readonly HttpClient _httpClient;
        private readonly IOptions<FeedCleanerOptions> _options;

        public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Updates assets that are now available from one of the non-stable feeds,
        /// delete those package versions from the stable feeds and delete any feeds 
        /// where all packages versions have been deleted every day at 2 AM.
        /// </summary>
        [CronSchedule("0 0 2 1/1 * ? *", TimeZones.PST)]
        public async Task CleanManagedFeedsAsync()
        {
            if (Options.Enabled)
            {
                Dictionary<string, Dictionary<string, HashSet<string>>> packagesInReleaseFeeds =
                    await GetPackagesForReleaseFeedsAsync();

                foreach (var azdoAccount in Options.AzdoAccounts)
                {
                    var azdoClient = AzureDevOpsClients[azdoAccount];
                    List<AzureDevOpsFeed> allFeeds = await azdoClient.GetFeedsAsync(azdoAccount);
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
                            Logger.LogError(ex, $"Something failed while trying to update the released packages in feed {feed.Name}");
                        } 
                    }
                }
            }
            else
            {
                Logger.LogInformation("Feed cleaner service is disabled in this environment");
            }
        }

        /// <summary>
        /// Returns an Azure DevOps client for a given account
        /// </summary>
        /// <param name="account">Azure DevOps account that the client will get its token from</param>
        /// <returns>Azure DevOps client for the given account</returns>
        private AzureDevOpsClient GetAzureDevOpsClientForAccountAsync(string account)
        {
            IAzureDevOpsTokenProvider azdoTokenProvider = Context.GetService<IAzureDevOpsTokenProvider>();
            string accessToken = azdoTokenProvider.GetTokenForAccount(account).GetAwaiter().GetResult();

            // FeedCleaner does not need Git, or a temporaryRepositoryPath
            return new AzureDevOpsClient(null, accessToken, Logger, null);
        }

        /// <summary>
        /// Get a mapping of feed -> (package, versions) for the release feeds so it
        /// can be easily queried whether a version of a package is in a feed.
        /// </summary>
        /// <returns>Mapping of packages to versions for the release feeds.</returns>
        private async Task<Dictionary<string, Dictionary<string, HashSet<string>>>> GetPackagesForReleaseFeedsAsync()
        {
            var packagesWithVersionsInReleaseFeeds = new Dictionary<string, Dictionary<string, HashSet<string>>>();
            IEnumerable<(string account, string project, string feedName)> dotnetManagedFeeds = Options.ReleasePackageFeeds;
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
        private static string ComputeAzureArtifactsNuGetFeedUrl(string feedName, string account, string project = null)
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
            var azdoClient = AzureDevOpsClients[account];
            var packagesWithVersions = new Dictionary<string, HashSet<string>>();
            List<AzureDevOpsPackage> packagesInFeed = await azdoClient.GetPackagesForFeedAsync(account, project, feedName);
            foreach (AzureDevOpsPackage package in packagesInFeed)
            {
                packagesWithVersions.Add(package.Name, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                packagesWithVersions[package.Name].UnionWith(package.Versions?.Where(v=> !v.IsDeleted).Select(v => v.Version));
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
                var matchingAssets = Context.Assets
                    .Include(a => a.Locations)
                    .Where(a => a.Name == package.Name &&
                        a.Version == version.Version).AsEnumerable();

                var matchingAsset = matchingAssets.FirstOrDefault(
                    a => a.Locations.Any(l => l.Location.Contains(feed.Name)));

                if (matchingAsset == null)
                {
                    Logger.LogError($"Unable to find asset {package.Name}.{version.Version} in feed {feed.Name} in BAR. " +
                        $"Unable to determine if it was released or update its locations.");
                    continue;
                }
                else
                {
                    if (matchingAsset.Locations.Any(l => l.Location == FeedConstants.NuGetOrgLocation ||
                        dotnetFeedsPackageMapping.Any(f => l.Location == f.Key)))
                    {
                        Logger.LogInformation($"Package {package.Name}.{version.Version} is already present in a public location.");
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
                            Logger.LogInformation(e,$"Failed to determine if package {package.Name}.{version.Version} is present in NuGet.org");
                        }


                        if (feedsWherePackageIsAvailable.Count > 0)
                        {
                            releasedVersions.Add(version.Version);
                            foreach (string feedToAdd in feedsWherePackageIsAvailable)
                            {
                                Logger.LogInformation($"Found package {package.Name}.{version.Version} in " +
                                $"{feedToAdd}, adding location to asset.");

                                matchingAsset.Locations.Add(new AssetLocation()
                                {
                                    Location = feedToAdd,
                                    Type = LocationType.NugetFeed
                                });
                                await Context.SaveChangesAsync();
                            }
                        }
                        else
                        {
                            Logger.LogInformation($"Unable to find {package.Name}.{version} in any of the release feeds");
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
            var azdoClient = AzureDevOpsClients[feed.Account];
            foreach (string version in versionsToDelete)
            {
                try
                {
                    Logger.LogInformation($"Deleting package {packageName}.{version} from feed {feed.Name}");

                    await azdoClient.DeleteNuGetPackageVersionFromFeedAsync(feed.Account,
                        feed.Project?.Name,
                        feed.Name,
                        packageName,
                        version);
                }
                catch (HttpRequestException e)
                {
                    Logger.LogError(e, $"There was an error attempting to delete package {packageName}.{version} from the {feed.Name} feed. Skipping...");
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
            List<string> feeds = new List<string>();
            foreach ((string feedName, Dictionary<string, HashSet<string>> packages) in packageMappings)
            {
                if (packages.TryGetValue(name, out HashSet<string> versions) && versions.Contains(version))
                {
                    feeds.Add(feedName);
                }
            }

            return feeds;
        }

        /// <summary>
        /// Checks whether a package is available in NuGet.org
        /// by using the package registration URL for a given package + version combination.
        /// </summary>
        /// <param name="name">Package to search for</param>
        /// <param name="version">Version to search for</param>
        /// <returns>True if the package is available in the NuGet.org registry, false if not</returns>
        private async Task<bool> IsPackageAvailableInNugetOrgAsync(string name, string version)
        {
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync($"{FeedConstants.NuGetOrgRegistrationBaseUrl}/{name.ToLower()}/{version}.json"))
                {
                    response.EnsureSuccessStatusCode();
                }
                return true;
            }
            catch (HttpRequestException e) when (e.Message.Contains("404 (Not Found)"))
            {
                Logger.LogInformation($"Unable to find {name}.{version} in nuget.org.");
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
            feed.Packages = await AzureDevOpsClients[feed.Account].GetPackagesForFeedAsync(feed.Account, feed.Project?.Name, feed.Name);
        }

        /// <summary>
        /// Checks whether a feed is empty
        /// (all the packages in the feed have had their versions deleted)
        /// </summary>
        /// <param name="feed"></param>
        /// <returns>true if the feed is empty, false otherwise</returns>
        private static bool IsFeedEmpty(AzureDevOpsFeed feed)
        {
            return feed.Packages.Count == 0 || feed.Packages.All(p => p.Versions.All(v => v.IsDeleted));
        }
    }
}
