// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Build.Framework;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using MSBuild = Microsoft.Build.Utilities;

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

                    BuildData finalBuild = MergeBuildManifests(buildsManifestMetadata);

                    IMaestroApi client = ApiFactory.GetAuthenticated(MaestroApiEndpoint, BuildAssetRegistryToken);

                    var deps = await GetBuildDependenciesAsync(client, cancellationToken);
                    Log.LogMessage(MessageImportance.High, "Calculated Dependencies:");
                    foreach (var dep in deps)
                    {
                        Log.LogMessage(MessageImportance.High, $"    {dep.BuildId}, IsProduct: {dep.IsProduct}");
                    }
                    finalBuild.Dependencies = await GetBuildDependenciesAsync(client, cancellationToken);

                    Client.Models.Build recordedBuild = await client.Builds.CreateAsync(finalBuild, cancellationToken);

                    Log.LogMessage(MessageImportance.High, $"Metadata has been pushed. Build id in the Build Asset Registry is '{recordedBuild.Id}'");
                }
            }
            catch (Exception exc)
            {
                Log.LogErrorFromException(exc, true, true, null);
            }

            return !Log.HasLoggedErrors;
        }

        private async Task<IImmutableList<BuildRef>> GetBuildDependenciesAsync(
            IMaestroApi client,
            CancellationToken cancellationToken)
        {
            var logger = new MSBuildLogger(Log);
            var local = new Local(logger);
            IEnumerable<DependencyDetail> dependencies = await local.GetDependenciesAsync();
            var builds = new Dictionary<int, bool>();
            foreach (var dep in dependencies)
            {
                var buildId = await GetBuildId(dep, client, cancellationToken);
                if (buildId == null)
                {
                    Log.LogWarning($"Asset '{dep.Name}@{dep.Version}' not found in BAR, ignoring.");
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

            return builds.Select(t => new BuildRef(t.Key, t.Value)).ToImmutableList();
        }

        private async Task<int?> GetBuildId(DependencyDetail dep, IMaestroApi client, CancellationToken cancellationToken)
        {
            var assets = await client.Assets.ListAssetsAsync(name: dep.Name, version: dep.Version, cancellationToken: cancellationToken);
            return assets.OrderByDescending(a => a.Id).FirstOrDefault()?.BuildId;
        }

        private string GetVersion(string assetId)
        {
            return VersionManager.GetVersion(assetId);
        }

        private List<BuildData> GetBuildManifestsMetadata(
            string manifestsFolderPath,
            CancellationToken cancellationToken)
        {
            var buildsManifestMetadata = new List<BuildData>();

            foreach (string manifestPath in Directory.GetFiles(manifestsFolderPath))
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
                            (manifest.InitialAssetsLocation == null) ? AssetLocationDataType.NugetFeed : AssetLocationDataType.Container,
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
                            AssetLocationDataType.Container, 
                            blob.NonShipping);
                    }

                    // The AzureDevOps properties can be null in the Manifest, but maestro needs them. Read them from the environment if they are null in the manifest.
                    var buildInfo = new BuildData(
                        commit: manifest.Commit,
                        azureDevOpsAccount: manifest.AzureDevOpsAccount ?? GetAzDevAccount(),
                        azureDevOpsProject: manifest.AzureDevOpsProject ?? GetAzDevProject(),
                        azureDevOpsBuildNumber: manifest.AzureDevOpsBuildNumber ?? GetAzDevBuildNumber(),
                        azureDevOpsRepository: manifest.AzureDevOpsRepository ?? GetAzDevRepository(),
                        azureDevOpsBranch: manifest.AzureDevOpsBranch ?? GetAzDevBranch(),
                        publishUsingPipelines: PublishUsingPipelines)
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
                return uri.AbsolutePath.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries).First();
            }

            return uri.Host.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries).First();
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
        /// <param name="assetLocationType">Type of location</param>
        /// <param name="nonShipping">If true, the asset is not intended for end customers</param>
        private void AddAsset(List<AssetData> assets, string assetName, string version, string location, AssetLocationDataType assetLocationType, bool nonShipping)
        {
            var locations = ImmutableList.Create<AssetLocationData>();
            if (location != null)
            {
                locations.Add(new AssetLocationData(assetLocationType)
                {
                    Location = location,
                });
            }

            assets.Add(new AssetData(nonShipping)
            {
                Locations = locations,
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

            using (var client = new HttpClient())
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
