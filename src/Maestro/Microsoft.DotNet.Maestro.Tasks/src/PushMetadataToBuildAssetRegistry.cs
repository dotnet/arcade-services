// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MSBuild = Microsoft.Build.Utilities;
using Microsoft.DotNet.VersionTools.BuildManifest;

namespace Microsoft.DotNet.Maestro.Tasks
{
    public class PushMetadataToBuildAssetRegistry : MSBuild.Task, ICancelableTask
    {
        [Required]
        public string ManifestsPath { get; set; }

        [Required]
        public string BuildAssetRegistryToken { get; set; }

        [Required]
        public string MaestroApiEndpoint { get; set; }

        public bool PublishUsingPipelines { get; set; } = false;

        private bool IsStableBuild { get; set; } = false;

        public string RepoRoot { get; set; }

        private const string SearchPattern = "*.xml";
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public void Cancel()
        {
            _tokenSource.Cancel();
        }

        public override bool Execute()
        {
            ExecuteAsync().GetAwaiter().GetResult();
            return !Log.HasLoggedErrors;
        }

        public async Task ExecuteAsync()
        {
            await PushMetadataAsync(_tokenSource.Token);
        }

        public async Task<bool> PushMetadataAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log.LogMessage(MessageImportance.High, "Starting build metadata push to the Build Asset Registry...");

                if (!Directory.Exists(ManifestsPath))
                {
                    Log.LogError($"Required folder '{ManifestsPath}' does not exist.");
                }
                else
                {
                    List<BuildData> buildsManifestMetadata = GetBuildManifestsMetadata(ManifestsPath, cancellationToken);

                    if (buildsManifestMetadata.Count == 0)
                    {
                        Log.LogError($"No build manifests found matching the search pattern {SearchPattern} in {ManifestsPath}");
                        return !Log.HasLoggedErrors;
                    }

                    BuildData finalBuild = MergeBuildManifests(buildsManifestMetadata);

                    IMaestroApi client = ApiFactory.GetAuthenticated(MaestroApiEndpoint, BuildAssetRegistryToken);

                    var deps = await GetBuildDependenciesAsync(client, cancellationToken);
                    Log.LogMessage(MessageImportance.High, "Calculated Dependencies:");
                    foreach (var dep in deps)
                    {
                        Log.LogMessage(MessageImportance.High, $"    {dep.BuildId}, IsProduct: {dep.IsProduct}");
                    }
                    finalBuild.Dependencies = deps;

                    Client.Models.Build recordedBuild = await client.Builds.CreateAsync(finalBuild, cancellationToken);

                    Log.LogMessage(MessageImportance.High, $"Metadata has been pushed. Build id in the Build Asset Registry is '{recordedBuild.Id}'");

                    // Only 'create' the AzDO (VSO) variables if running in an AzDO build
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDID")))
                    {
                        IEnumerable<DefaultChannel> defaultChannels = await GetBuildDefaultChannelsAsync(client, recordedBuild);

                        HashSet<int> targetChannelIds = new HashSet<int>(defaultChannels.Select(dc => dc.Channel.Id));

                        var defaultChannelsStr = "[" + string.Join("][", targetChannelIds) + "]";
                        Log.LogMessage(MessageImportance.High, $"Determined build will be added to the following channels: { defaultChannelsStr}");

                        Console.WriteLine($"##vso[task.setvariable variable=BARBuildId]{recordedBuild.Id}");
                        Console.WriteLine($"##vso[task.setvariable variable=DefaultChannels]{defaultChannelsStr}");
                        Console.WriteLine($"##vso[task.setvariable variable=IsStableBuild]{IsStableBuild}");
                    }
                }
            }
            catch (Exception exc)
            {
                Log.LogErrorFromException(exc, true, true, null);
            }

            return !Log.HasLoggedErrors;
        }

        private async Task<IEnumerable<DefaultChannel>> GetBuildDefaultChannelsAsync(IMaestroApi client, Client.Models.Build recordedBuild)
        {
            var defaultChannels = new List<DefaultChannel>();
            if (recordedBuild.GitHubBranch != null && recordedBuild.GitHubRepository != null)
            {
                defaultChannels.AddRange(
                    await client.DefaultChannels.ListAsync(
                        branch: recordedBuild.GitHubBranch,
                        channelId: null,
                        enabled: true,
                        repository: recordedBuild.GitHubRepository
                    ));
            }

            if (recordedBuild.AzureDevOpsBranch != null && recordedBuild.AzureDevOpsRepository != null)
            {
                defaultChannels.AddRange(
                    await client.DefaultChannels.ListAsync(
                        branch: recordedBuild.AzureDevOpsBranch,
                        channelId: null,
                        enabled: true,
                        repository: recordedBuild.AzureDevOpsRepository
                    ));
            }

            Log.LogMessage(MessageImportance.High, "Found the following default channels:");
            foreach (var defaultChannel in defaultChannels)
            {
                Log.LogMessage(
                    MessageImportance.High,
                    $"    {defaultChannel.Repository}@{defaultChannel.Branch} " +
                    $"=> ({defaultChannel.Channel.Id}) {defaultChannel.Channel.Name}");
            }
            return defaultChannels;
        }

        private async Task<IImmutableList<BuildRef>> GetBuildDependenciesAsync(
            IMaestroApi client,
            CancellationToken cancellationToken)
        {
            var logger = new MSBuildLogger(Log);
            var local = new Local(logger, RepoRoot);
            IEnumerable<DependencyDetail> dependencies = await local.GetDependenciesAsync();
            var builds = new Dictionary<int, bool>();
            var assetCache = new Dictionary<(string name, string version, string commit), int>();
            var buildCache = new Dictionary<int, Client.Models.Build>();
            foreach (var dep in dependencies)
            {
                var buildId = await GetBuildId(dep, client, buildCache, assetCache, cancellationToken);
                if (buildId == null)
                {
                    Log.LogMessage(
                        MessageImportance.High,
                        $"Asset '{dep.Name}@{dep.Version}' not found in BAR, most likely this is an external dependency, ignoring...");
                    continue;
                }

                Log.LogMessage(
                    MessageImportance.Normal,
                    $"Dependency '{dep.Name}@{dep.Version}' found in build {buildId.Value}");

                var isProduct = dep.Type == DependencyType.Product;

                if (!builds.ContainsKey(buildId.Value))
                {
                    builds[buildId.Value] = isProduct;
                }
                else
                {
                    builds[buildId.Value] = isProduct || builds[buildId.Value];
                }
            }

            return builds.Select(t => new BuildRef(t.Key, t.Value, 0)).ToImmutableList();
        }

        private static async Task<int?> GetBuildId(DependencyDetail dep, IMaestroApi client, Dictionary<int, Client.Models.Build> buildCache,
            Dictionary<(string name, string version, string commit), int> assetCache, CancellationToken cancellationToken)
        {
            if (assetCache.TryGetValue((dep.Name, dep.Version, dep.Commit), out int value))
            {
                return value;
            }
            var assets = client.Assets.ListAssetsAsync(name: dep.Name, version: dep.Version, cancellationToken: cancellationToken);
            List<Asset> matchingAssetsFromSameSha = new List<Asset>();

            // Filter out those assets which do not have matching commits
            await foreach (Asset asset in assets)
            {
                if (!buildCache.TryGetValue(asset.BuildId, out Client.Models.Build producingBuild))
                {
                    producingBuild = await client.Builds.GetBuildAsync(asset.BuildId);
                    buildCache.Add(asset.BuildId, producingBuild);
                }

                if (producingBuild.Commit == dep.Commit)
                {
                    matchingAssetsFromSameSha.Add(asset);
                }
            }

            var buildId = matchingAssetsFromSameSha.OrderByDescending(a => a.Id).FirstOrDefault()?.BuildId;
            if (!buildId.HasValue)
            {
                return null;
            }

            // Commonly, if a repository has a dependency on an asset from a build, more dependencies will be to that same build
            // lets fetch all assets from that build to save time later.
            var build = await client.Builds.GetBuildAsync(buildId.Value, cancellationToken);
            foreach (var asset in build.Assets)
            {
                if (!assetCache.ContainsKey((asset.Name, asset.Version, build.Commit)))
                {
                    assetCache.Add((asset.Name, asset.Version, build.Commit), build.Id);
                }
            }

            return buildId;
        }

        private string GetVersion(string assetId)
        {
            return VersionIdentifier.GetVersion(assetId);
        }

        private List<BuildData> GetBuildManifestsMetadata(
            string manifestsFolderPath,
            CancellationToken cancellationToken)
        {
            var buildsManifestMetadata = new List<BuildData>();

            foreach (string manifestPath in Directory.GetFiles(manifestsFolderPath, SearchPattern, SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var xmlSerializer = new XmlSerializer(typeof(Manifest));
                using (var stream = new FileStream(manifestPath, FileMode.Open))
                {
                    var manifest = (Manifest)xmlSerializer.Deserialize(stream);

                    var assets = new List<AssetData>();

                    foreach (Package package in manifest.Packages)
                    {
                        AddAsset(
                            assets,
                            package.Id,
                            package.Version,
                            manifest.InitialAssetsLocation ?? manifest.Location,
                            (manifest.InitialAssetsLocation == null) ? LocationType.NugetFeed : LocationType.Container,
                            package.NonShipping);
                    }

                    foreach (Blob blob in manifest.Blobs)
                    {
                        string version = GetVersion(blob.Id);

                        if (string.IsNullOrEmpty(version))
                        {
                            Log.LogWarning($"Version could not be extracted from '{blob.Id}'");
                            version = string.Empty;
                        }

                        AddAsset(
                            assets,
                            blob.Id,
                            version,
                            manifest.InitialAssetsLocation ?? manifest.Location,
                            LocationType.Container,
                            blob.NonShipping);
                    }

                    // For now we aren't persisting this property, so we just record this info in the task scope
                    IsStableBuild = bool.Parse(manifest.IsStable.ToLower());

                    // The AzureDevOps properties can be null in the Manifest, but maestro needs them. Read them from the environment if they are null in the manifest.
                    var buildInfo = new BuildData(
                        commit: manifest.Commit,
                        azureDevOpsAccount: manifest.AzureDevOpsAccount ?? GetAzDevAccount(),
                        azureDevOpsProject: manifest.AzureDevOpsProject ?? GetAzDevProject(),
                        azureDevOpsBuildNumber: manifest.AzureDevOpsBuildNumber ?? GetAzDevBuildNumber(),
                        azureDevOpsRepository: manifest.AzureDevOpsRepository ?? GetAzDevRepository(),
                        azureDevOpsBranch: manifest.AzureDevOpsBranch ?? GetAzDevBranch(),
                        publishUsingPipelines: PublishUsingPipelines,
                        released: false)
                    {
                        Assets = assets.ToImmutableList(),
                        AzureDevOpsBuildId = manifest.AzureDevOpsBuildId ?? GetAzDevBuildId(),
                        AzureDevOpsBuildDefinitionId = manifest.AzureDevOpsBuildDefinitionId ?? GetAzDevBuildDefinitionId(),
                        GitHubRepository = manifest.Name,
                        GitHubBranch = manifest.Branch,
                    };

                    buildsManifestMetadata.Add(buildInfo);
                }
            }

            return buildsManifestMetadata;
        }

        private string GetEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException($"Required Environment variable {key} not found.");
            }

            return value;
        }

        private string GetAzDevAccount()
        {
            var uri = new Uri(GetEnv("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"));
            if (uri.Host == "dev.azure.com")
            {
                return uri.AbsolutePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).First();
            }

            return uri.Host.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).First();
        }

        private string GetAzDevProject()
        {
            return GetEnv("SYSTEM_TEAMPROJECT");
        }

        private string GetAzDevBuildNumber()
        {
            return GetEnv("BUILD_BUILDNUMBER");
        }

        private string GetAzDevRepository()
        {
            return GetEnv("BUILD_REPOSITORY_URI");
        }

        private string GetAzDevBranch()
        {
            return GetEnv("BUILD_SOURCEBRANCH");
        }

        private int GetAzDevBuildId()
        {
            return int.Parse(GetEnv("BUILD_BUILDID"));
        }

        private int GetAzDevBuildDefinitionId()
        {
            return int.Parse(GetEnv("SYSTEM_DEFINITIONID"));
        }

        /// <summary>
        ///     Add a new asset to the list of assets that will be uploaded to BAR
        /// </summary>
        /// <param name="assets">List of assets</param>
        /// <param name="assetName">Name of new asset</param>
        /// <param name="version">Version of asset</param>
        /// <param name="location">Location of asset</param>
        /// <param name="locationType">Type of location</param>
        /// <param name="nonShipping">If true, the asset is not intended for end customers</param>
        private void AddAsset(List<AssetData> assets, string assetName, string version, string location, LocationType locationType, bool nonShipping)
        {
            assets.Add(new AssetData(nonShipping)
            {
                Locations = (location == null) ? null : ImmutableList.Create(new AssetLocationData(locationType)
                {
                    Location = location,
                }),
                Name = assetName,
                Version = version,
            });
        }

        private BuildData MergeBuildManifests(List<BuildData> buildsMetadata)
        {
            BuildData mergedBuild = buildsMetadata[0];

            for (int i = 1; i < buildsMetadata.Count; i++)
            {
                BuildData build = buildsMetadata[i];

                if (mergedBuild.AzureDevOpsBranch != build.AzureDevOpsBranch ||
                    mergedBuild.AzureDevOpsBuildNumber != build.AzureDevOpsBuildNumber ||
                    mergedBuild.Commit != build.Commit ||
                    mergedBuild.AzureDevOpsRepository != build.AzureDevOpsRepository)
                {
                    throw new Exception("Can't merge if one or more manifests have different branch, build number, commit or repository values.");
                }

                mergedBuild.Assets = mergedBuild.Assets.AddRange(build.Assets);
            }

            // Remove duplicated assets based on the top level properties of the asset.
            // The AssetLocations property isn't set at this point yet.
            mergedBuild.Assets = mergedBuild.Assets.Distinct(new AssetDataComparer()).ToImmutableList();

            LookupForMatchingGitHubRepository(mergedBuild);

            return mergedBuild;
        }

        /// <summary>
        /// When we flow dependencies we expect source and target repos to be the same i.e github.com or dev.azure.com/dnceng. 
        /// When this task is executed the repository is an Azure DevOps repository even though the real source is GitHub 
        /// since we just mirror the code. When we detect an Azure DevOps repository we check if the latest commit exist in 
        /// GitHub to determine if the source is GitHub or not. If the commit exists in the repo we transform the Url from 
        /// Azure DevOps to GitHub. If not we continue to work with the original Url.
        /// </summary>
        /// <returns></returns>
        private void LookupForMatchingGitHubRepository(BuildData mergedBuild)
        {
            if (mergedBuild == null)
            {
                throw new ArgumentNullException(nameof(mergedBuild));
            }

            using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
            {
                string repoIdentity = string.Empty;
                string gitHubHost = "github.com";

                if (!Uri.TryCreate(mergedBuild.AzureDevOpsRepository, UriKind.Absolute, out Uri repoAddr))
                {
                    throw new Exception($"Can't parse the repository URL: {mergedBuild.AzureDevOpsRepository}");
                }

                if (repoAddr.Host.Equals(gitHubHost, StringComparison.OrdinalIgnoreCase))
                {
                    repoIdentity = repoAddr.AbsolutePath.Trim('/');
                }
                else
                {
                    string[] segments = mergedBuild.AzureDevOpsRepository.Split('/');
                    string repoName = segments[segments.Length - 1];
                    int index = repoName.IndexOf('-');

                    StringBuilder builder = new StringBuilder(repoName);
                    builder[index] = '/';

                    repoIdentity = builder.ToString();
                }

                client.BaseAddress = new Uri($"https://api.{gitHubHost}");
                client.DefaultRequestHeaders.Add("User-Agent", "PushToBarTask");

                HttpResponseMessage response = client.GetAsync($"/repos/{repoIdentity}/commits/{mergedBuild.Commit}").Result;

                if (response.IsSuccessStatusCode)
                {
                    mergedBuild.GitHubRepository = $"https://github.com/{repoIdentity}";
                    mergedBuild.GitHubBranch = mergedBuild.AzureDevOpsBranch;
                }
                else
                {
                    mergedBuild.GitHubRepository = null;
                    mergedBuild.GitHubBranch = null;
                }
            }
        }
    }
}
