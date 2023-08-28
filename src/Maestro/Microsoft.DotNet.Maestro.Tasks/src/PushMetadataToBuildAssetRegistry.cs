// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Tasks.Proxies;
using Microsoft.DotNet.VersionTools.BuildManifest.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Serialization;
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

        private bool IsStableBuild { get; set; } = false;

        private bool IsReleaseOnlyPackageVersion { get; set; } = false;

        public string RepoRoot { get; set; }

        public string AssetVersion { get; set; }

        [Output] 
        public int BuildId { get; set; }

        private const string SearchPattern = "*.xml";
        private const string MergedManifestFileName = "MergedManifest.xml";
        private const string NoCategory = "NONE";
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private string GitHubRepository = "";
        private string GitHubBranch = "";

        // Set up proxy objects to allow unit test mocking
        internal IVersionIdentifierProxy versionIdentifier = new VersionIdentifierProxy();
        internal IGetEnvProxy getEnvProxy = new GetEnvProxy();

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
                    //get the list of manifests
                    List<Manifest> parsedManifests = GetParsedManifests(ManifestsPath, cancellationToken);

                    if (parsedManifests.Count == 0)
                    {
                        Log.LogError(
                            $"No manifests found matching the search pattern {SearchPattern} in {ManifestsPath}");
                        return !Log.HasLoggedErrors;
                    }

                    Manifest manifest;
                    //check if the manifest have any duplicate packages and blobs
                    if (parsedManifests.Count > 1)
                    {
                        manifest = MergeManifests(parsedManifests);
                    }
                    else
                    {
                        manifest = parsedManifests[0];
                    }

                    List<SigningInformation> signingInformation = new List<SigningInformation>();
                    foreach (var m in parsedManifests)
                    {
                        if (m.SigningInformation != null)
                        {
                            signingInformation.Add(m.SigningInformation);
                        }
                    }

                    //get packages blobs and signing info 
                    (List<PackageArtifactModel> packages,
                        List<BlobArtifactModel> blobs) = GetPackagesAndBlobsInfo(manifest);

                    //create merged buildModel to create the merged manifest
                    BuildModel modelForManifest =
                        CreateMergedManifestBuildModel(packages, blobs, manifest);


                    //add manifest as an asset to the buildModel
                    var mergedManifestAsset = GetManifestAsAsset(blobs, MergedManifestFileName);
                    modelForManifest.Artifacts.Blobs.Add(mergedManifestAsset);
                    
                    SigningInformation finalSigningInfo = MergeSigningInfo(signingInformation);

                    // push the merged manifest, this is required for only publishingVersion 3 and above
                    if (manifest.PublishingVersion >= 3)
                    {
                        PushMergedManifest(modelForManifest, finalSigningInfo);
                    }

                    // populate buildData and assetData using merged manifest data 
                    BuildData buildData = GetMaestroBuildDataFromMergedManifest(modelForManifest, manifest, cancellationToken);

                    IMaestroApi client = ApiFactory.GetAuthenticated(MaestroApiEndpoint, BuildAssetRegistryToken);

                    var deps = await GetBuildDependenciesAsync(client, cancellationToken);
                    Log.LogMessage(MessageImportance.High, "Calculated Dependencies:");
                    foreach (var dep in deps)
                    {
                        Log.LogMessage(MessageImportance.High, $"    {dep.BuildId}, IsProduct: {dep.IsProduct}");
                    }

                    buildData.Dependencies = deps;
                    LookupForMatchingGitHubRepository(manifest);
                    buildData.GitHubBranch = GitHubBranch;
                    buildData.GitHubRepository = GitHubRepository;

                    Client.Models.Build recordedBuild = await client.Builds.CreateAsync(buildData, cancellationToken);
                    BuildId = recordedBuild.Id;

                    Log.LogMessage(MessageImportance.High,
                        $"Metadata has been pushed. Build id in the Build Asset Registry is '{recordedBuild.Id}'");
                    Console.WriteLine($"##vso[build.addbuildtag]BAR ID - {recordedBuild.Id}");

                    // Only 'create' the AzDO (VSO) variables if running in an AzDO build
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDID")))
                    {
                        IEnumerable<DefaultChannel> defaultChannels =
                            await GetBuildDefaultChannelsAsync(client, recordedBuild);

                        HashSet<int> targetChannelIds = new HashSet<int>(defaultChannels.Select(dc => dc.Channel.Id));

                        var defaultChannelsStr = "[" + string.Join("][", targetChannelIds) + "]";
                        Log.LogMessage(MessageImportance.High,
                            $"Determined build will be added to the following channels: {defaultChannelsStr}");

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

        private async Task<IEnumerable<DefaultChannel>> GetBuildDefaultChannelsAsync(IMaestroApi client,
            Client.Models.Build recordedBuild)
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
            var local = new Local(new RemoteConfiguration(), logger, RepoRoot);
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

        private static async Task<int?> GetBuildId(DependencyDetail dep, IMaestroApi client,
            Dictionary<int, Client.Models.Build> buildCache,
            Dictionary<(string name, string version, string commit), int> assetCache,
            CancellationToken cancellationToken)
        {
            if (assetCache.TryGetValue((dep.Name, dep.Version, dep.Commit), out int value))
            {
                return value;
            }

            var assets = client.Assets.ListAssetsAsync(name: dep.Name, version: dep.Version,
                cancellationToken: cancellationToken);
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
            return versionIdentifier.GetVersion(assetId);
        }

        internal List<Manifest> GetParsedManifests(
            string manifestsFolderPath,
            CancellationToken cancellationToken)
        {
            List<Manifest> parsedManifests = new List<Manifest>();

            foreach (string manifestPath in Directory.GetFiles(manifestsFolderPath, SearchPattern,
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                XmlSerializer xmlSerializer = new XmlSerializer(typeof(Manifest));
                using FileStream stream = new FileStream(manifestPath, FileMode.Open);
                Manifest manifest = (Manifest)xmlSerializer.Deserialize(stream);
                parsedManifests.Add(manifest);
            }

            return parsedManifests;
        }


        internal BuildData GetMaestroBuildDataFromMergedManifest(
            BuildModel buildModel,
            Manifest manifest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var assets = new List<AssetData>();

            IsStableBuild = bool.Parse(manifest.IsStable.ToLower());

            // The AzureDevOps properties can be null in the Manifest, but maestro needs them. Read them from the environment if they are null in the manifest.
            var buildInfo = new BuildData(
                commit: manifest.Commit,
                azureDevOpsAccount: manifest.AzureDevOpsAccount ?? GetAzDevAccount(),
                azureDevOpsProject: manifest.AzureDevOpsProject ?? GetAzDevProject(),
                azureDevOpsBuildNumber: manifest.AzureDevOpsBuildNumber ?? GetAzDevBuildNumber(),
                azureDevOpsRepository: manifest.AzureDevOpsRepository ?? GetAzDevRepository(),
                azureDevOpsBranch: manifest.AzureDevOpsBranch ?? GetAzDevBranch(),
                stable: IsStableBuild,
                released: false)
            {
                Assets = assets.ToImmutableList(),
                AzureDevOpsBuildId = manifest.AzureDevOpsBuildId ?? GetAzDevBuildId(),
                AzureDevOpsBuildDefinitionId = manifest.AzureDevOpsBuildDefinitionId ?? GetAzDevBuildDefinitionId(),
                GitHubRepository = manifest.Name,
                GitHubBranch = manifest.Branch,
            };

            foreach (var package in buildModel.Artifacts.Packages)
            {
                AddAsset(
                    assets,
                    package.Id,
                    package.Version,
                    manifest.InitialAssetsLocation ?? manifest.Location,
                    (manifest.InitialAssetsLocation == null) ? LocationType.NugetFeed : LocationType.Container,
                    package.NonShipping);
            }

            foreach (var blob in buildModel.Artifacts.Blobs)
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

            buildInfo.Assets = buildInfo.Assets.AddRange(assets);

            return buildInfo;
        }

        private string GetAzDevAccount()
        {
            var uri = new Uri(getEnvProxy.GetEnv("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"));
            if (uri.Host == "dev.azure.com")
            {
                return uri.AbsolutePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).First();
            }

            return uri.Host.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).First();
        }

        private string GetAzDevProject()
        {
            return getEnvProxy.GetEnv("SYSTEM_TEAMPROJECT");
        }

        private string GetAzDevBuildNumber()
        {
            return getEnvProxy.GetEnv("BUILD_BUILDNUMBER");
        }

        private string GetAzDevRepository()
        {
            return getEnvProxy.GetEnv("BUILD_REPOSITORY_URI");
        }

        private string GetAzDevRepositoryName()
        {
            return getEnvProxy.GetEnv("BUILD_REPOSITORY_NAME");
        }

        private string GetAzDevBranch()
        {
            return getEnvProxy.GetEnv("BUILD_SOURCEBRANCH");
        }

        private int GetAzDevBuildId()
        {
            return int.Parse(getEnvProxy.GetEnv("BUILD_BUILDID"));
        }

        private int GetAzDevBuildDefinitionId()
        {
            return int.Parse(getEnvProxy.GetEnv("SYSTEM_DEFINITIONID"));
        }

        private string GetAzDevCommit()
        {
            return getEnvProxy.GetEnv("BUILD_SOURCEVERSION");
        }

        private string GetAzDevStagingDirectory()
        {
            return getEnvProxy.GetEnv("BUILD_STAGINGDIRECTORY");
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
        internal void AddAsset(List<AssetData> assets, string assetName, string version, string location,
            LocationType locationType, bool nonShipping)
        {
            assets.Add(new AssetData(nonShipping)
            {
                Locations = (location == null)
                    ? null
                    : ImmutableList.Create(new AssetLocationData(locationType)
                    {
                        Location = location,
                    }),
                Name = assetName,
                Version = version,
            });
        }

        internal (List<PackageArtifactModel>,
            List<BlobArtifactModel>) GetPackagesAndBlobsInfo(Manifest manifest)
        {
            List<PackageArtifactModel> packageArtifacts = new List<PackageArtifactModel>();
            List<BlobArtifactModel> blobArtifacts = new List<BlobArtifactModel>();

            foreach (var package in manifest.Packages)
            {
                PackageArtifactModel packageArtifact = new PackageArtifactModel()
                {
                    Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", package.NonShipping.ToString().ToLower() },
                    },
                    Id = package.Id,
                    Version = package.Version
                };

                packageArtifacts.Add(packageArtifact);
            }

            foreach (var blob in manifest.Blobs)
            {
                BlobArtifactModel blobArtifact = new BlobArtifactModel()
                {
                    Attributes = new Dictionary<string, string>
                    {
                        { "NonShipping", blob.NonShipping.ToString().ToLower() },
                        {
                            "Category",
                            !string.IsNullOrEmpty(blob.Category) ? blob.Category.ToString().ToUpper() : NoCategory
                        }
                    },
                    Id = blob.Id,
                };

                blobArtifacts.Add(blobArtifact);
            }

            return (packageArtifacts, blobArtifacts);

        }

        internal Manifest MergeManifests(List<Manifest> parsedManifests)
        {
            Manifest manifest = parsedManifests[0];

            for (int i = 1; i < parsedManifests.Count; i++)
            {
                Manifest nextManifest = parsedManifests[i];

                if (manifest.AzureDevOpsBranch != nextManifest.AzureDevOpsBranch ||
                    manifest.AzureDevOpsBuildNumber != nextManifest.AzureDevOpsBuildNumber ||
                    manifest.Commit != nextManifest.Commit ||
                    manifest.AzureDevOpsRepository != nextManifest.AzureDevOpsRepository)
                {
                    throw new Exception(
                        "Can't merge if one or more manifests have different branch, build number, commit, or repository values.");
                }

                manifest.Packages.AddRange(nextManifest.Packages);
                manifest.Blobs.AddRange(nextManifest.Blobs);
            }

            // Error out for any duplicated packages based on the top level properties of the package.
            var distinctPackages = manifest.Packages.DistinctBy(p => p.Id);
            if (distinctPackages.Count() < manifest.Packages.Count())
            {
                var dupes = manifest.Packages.GroupBy(x => new { x.Id, x.Version })
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToImmutableList();
                foreach (var dupe in dupes)
                {
                    Log.LogError($"Repeated Package entry: '{dupe.Id}' - '{dupe.Version}' ");
                }

                // throw to stop, as this is invalid.
                throw new InvalidOperationException(
                    "Duplicate package entries are not allowed for publishing to BAR, as this can cause race conditions and unexpected behavior");
            }

            // Error out for any duplicated blob based on the top level properties of the blob.
            var distinctBlobs = manifest.Blobs.DistinctBy(b => b.Id);
            if (distinctBlobs.Count() < manifest.Blobs.Count())
            {
                var dupes = manifest.Blobs.GroupBy(x => new { x.Id })
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToImmutableList();
                foreach (var dupe in dupes)
                {
                    Log.LogError($"Repeated Blob entry: '{dupe.Id}' ");
                }

                // throw to stop, as this is invalid.
                throw new InvalidOperationException(
                    "Duplicate blob entries are not allowed for publishing to BAR, as this can cause race conditions and unexpected behavior");
            }

            return manifest;
        }

        internal SigningInformation MergeSigningInfo(List<SigningInformation> signingInformation)
        {
            SigningInformation mergedInfo = null;

            if (signingInformation.Any())
            {
                foreach (SigningInformation signInfo in signingInformation)
                {
                    if (mergedInfo == null)
                    {
                        mergedInfo = signInfo;
                    }
                    else
                    {
                        mergedInfo.FileExtensionSignInfos.AddRange(signInfo.FileExtensionSignInfos);
                        mergedInfo.FileSignInfos.AddRange(signInfo.FileSignInfos);
                        mergedInfo.CertificatesSignInfo.AddRange(signInfo.CertificatesSignInfo);
                        mergedInfo.ItemsToSign.AddRange(signInfo.ItemsToSign);
                        mergedInfo.StrongNameSignInfos.AddRange(signInfo.StrongNameSignInfos);
                    }
                }

                mergedInfo.FileExtensionSignInfos =
                    new List<FileExtensionSignInfo>(
                        mergedInfo.FileExtensionSignInfos.Distinct(new FileExtensionSignInfoComparer()));
                mergedInfo.FileSignInfos =
                    new List<FileSignInfo>(mergedInfo.FileSignInfos.Distinct(new FileSignInfoComparer()));
                mergedInfo.CertificatesSignInfo =
                    new List<CertificatesSignInfo>(
                        mergedInfo.CertificatesSignInfo.Distinct(new CertificatesSignInfoComparer()));
                mergedInfo.ItemsToSign =
                    new List<ItemsToSign>(mergedInfo.ItemsToSign.Distinct(new ItemsToSignComparer()));
                mergedInfo.StrongNameSignInfos =
                    new List<StrongNameSignInfo>(
                        mergedInfo.StrongNameSignInfos.Distinct(new StrongNameSignInfoComparer()));
            }

            return mergedInfo;
        }

        /// <summary>
        /// When we flow dependencies we expect source and target repos to be the same i.e github.com or dev.azure.com/dnceng. 
        /// When this task is executed the repository is an Azure DevOps repository even though the real source is GitHub 
        /// since we just mirror the code. When we detect an Azure DevOps repository we check if the latest commit exists in 
        /// GitHub to determine if the source is GitHub or not. If the commit exists in the repo we transform the Url from 
        /// Azure DevOps to GitHub. If not we continue to work with the original Url.
        /// </summary>
        /// <returns></returns>
        private void LookupForMatchingGitHubRepository(Manifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
            {
                string repoIdentity = string.Empty;
                string gitHubHost = "github.com";

                if (!Uri.TryCreate(manifest.AzureDevOpsRepository, UriKind.Absolute, out Uri repoAddr))
                {
                    throw new Exception($"Can't parse the repository URL: {manifest.AzureDevOpsRepository}");
                }

                if (repoAddr.Host.Equals(gitHubHost, StringComparison.OrdinalIgnoreCase))
                {
                    repoIdentity = repoAddr.AbsolutePath.Trim('/');
                }
                else
                {
                    repoIdentity = GetGithubRepoName(manifest.AzureDevOpsRepository);
                }

                client.BaseAddress = new Uri($"https://api.{gitHubHost}");
                client.DefaultRequestHeaders.Add("User-Agent", "PushToBarTask");

                HttpResponseMessage response =
                    client.GetAsync($"/repos/{repoIdentity}/commits/{manifest.Commit}").Result;

                if (response.IsSuccessStatusCode)
                {
                    GitHubRepository = $"https://github.com/{repoIdentity}";
                    GitHubBranch = manifest.AzureDevOpsBranch;
                }
                else
                {
                    Log.LogMessage(MessageImportance.High,
                        $" Unable to translate AzDO to GitHub URL. HttpResponse: {response.StatusCode} {response.ReasonPhrase} for repoIdentity: {repoIdentity} and commit: {manifest.Commit}.");
                    GitHubRepository = null;
                    GitHubBranch = null;
                }
            }
        }

        /// <summary>
        /// Get repo name from the Azure DevOps repo url
        /// </summary>
        /// <param name="repoUrl"></param>
        /// <returns></returns>
        public string GetGithubRepoName(string repoUrl)
        {
            // In case the URL comes in ending with a '/', prevent an indexing exception
            repoUrl = repoUrl.TrimEnd('/');

            string[] segments = repoUrl.Split('/');
            string repoName = segments[segments.Length - 1].ToLower();

            if (repoUrl.Contains("DevDiv", StringComparison.OrdinalIgnoreCase)
                && repoName.EndsWith("-Trusted", StringComparison.OrdinalIgnoreCase))
            {
                repoName = repoName.Remove(repoName.LastIndexOf("-trusted"));
            }

            StringBuilder builder = new StringBuilder(repoName);
            int index = repoName.IndexOf('-');

            if (index > -1)
            {
                builder[index] = '/';
            }

            return builder.ToString();
        }

        /// <summary>
        /// Creates a merged manifest blob
        /// </summary>
        /// <param name="blobs">List of blobs extracted from merged manifest</param>
        /// <param name="manifestFileName">Merged manifest file name</param>
        /// <returns>A blob with data about the merged manifest</returns>
        internal BlobArtifactModel GetManifestAsAsset(List<BlobArtifactModel> blobs, string manifestFileName)
        {
            string repoName = GetAzDevRepositoryName().TrimEnd('/').Replace('/', '-');
            string Id = string.Empty;
            if (string.IsNullOrEmpty(AssetVersion))
            {
                var blob = blobs.Where(a => a.NonShipping).FirstOrDefault();
                if (blob != null)
                {
                    AssetVersion = blob.Id;
                    Id = AssetVersion;
                }
            }
            else
            {
                Id = $"assets/manifests/{repoName}/{AssetVersion}/{manifestFileName}";
            }

            var mergedManifest = new BlobArtifactModel()
            {
                Attributes = new Dictionary<string, string>()
                {
                    { "NonShipping", "true" }
                },
                Id = $"{Id}"
            };
            
            return mergedManifest;
        }

        /// <summary>
        /// Creates the build model from the list of packages, list of blobs and merged manifest
        /// </summary>
        /// <param name="packages">List of packages for the merged manifest</param>
        /// <param name="blobs">List of blobs for the merged manifest</param>
        /// <param name="manifest">Merged manifest</param>
        /// <returns>A BuildModel with all the assets is returned.</returns>
        internal BuildModel CreateMergedManifestBuildModel(
            List<PackageArtifactModel> packages,
            List<BlobArtifactModel> blobs,
            Manifest manifest)
        {
            BuildModel buildModel = new BuildModel(
                new BuildIdentity
                {

                    Attributes = new Dictionary<string, string>()
                    {
                        { "InitialAssetsLocation", manifest.InitialAssetsLocation },
                        { "AzureDevOpsBuildId", manifest.AzureDevOpsBuildId.ToString() },
                        { "AzureDevOpsBuildDefinitionId", manifest.AzureDevOpsBuildDefinitionId.ToString() },
                        { "AzureDevOpsAccount", manifest.AzureDevOpsAccount },
                        { "AzureDevOpsProject", manifest.AzureDevOpsProject },
                        { "AzureDevOpsBuildNumber", manifest.AzureDevOpsBuildNumber },
                        { "AzureDevOpsRepository", manifest.AzureDevOpsRepository },
                        { "AzureDevOpsBranch", manifest.AzureDevOpsBranch }
                    },
                    Name = GetAzDevRepositoryName(),
                    BuildId = GetAzDevBuildNumber(),
                    Branch = GetAzDevBranch(),
                    Commit = GetAzDevCommit(),
                    IsStable = bool.Parse(manifest.IsStable),
                    PublishingVersion = (PublishingInfraVersion)manifest.PublishingVersion,
                    IsReleaseOnlyPackageVersion = bool.Parse(manifest.IsReleaseOnlyPackageVersion)

                });

            buildModel.Artifacts.Blobs.AddRange(blobs);
            buildModel.Artifacts.Packages.AddRange(packages);

            return buildModel;
        }

        /// <summary>
        /// Pushed the merged manifest
        /// </summary>
        /// <param name="buildModel">Build Model that contains all the details about Build and assets</param>
        /// <param name="finalSigningInfo">Signing information</param>
        private void PushMergedManifest(BuildModel buildModel, SigningInformation finalSigningInfo)
        {
            string mergedManifestPath = Path.Combine(GetAzDevStagingDirectory(), MergedManifestFileName);

            XElement buildModelXml = buildModel.ToXml();

            if (finalSigningInfo != null)
            {
                buildModelXml.Add(XmlSerializationHelper.SigningInfoToXml(finalSigningInfo));
            }

            File.WriteAllText(mergedManifestPath, buildModelXml.ToString());

            Log.LogMessage(MessageImportance.High,
                        $"##vso[artifact.upload containerfolder=BlobArtifacts;artifactname=BlobArtifacts]{mergedManifestPath}");
        }
    }
}
