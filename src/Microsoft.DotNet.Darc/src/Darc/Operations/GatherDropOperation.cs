// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class DownloadedAsset
    {
        public Asset Asset { get; set; }
        public string SourceLocation { get; set; }
        public string TargetLocation { get; set; }
        public bool Successful { get; set; }
    }

    internal class DownloadedBuild
    {
        public Build Build { get; set; }
        public bool Successful { get; set; }
        public IEnumerable<DownloadedAsset> DownloadedAssets { get; set; }
        /// <summary>
        ///     Root output directory for this build.
        /// </summary>
        public string OutputDirectory { get; set; }
        /// <summary>
        ///     True if the output has any shipping assets.
        /// </summary>
        public bool AnyShippingAssets { get; set; }
    }

    internal class InputBuilds
    {
        public IEnumerable<Build> Builds { get; set; }
        public bool Successful { get; set; }
    }

    internal class GatherDropOperation : Operation
    {
        GatherDropCommandLineOptions _options;
        public GatherDropOperation(GatherDropCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        const string packagesSubPath = "packages";
        const string assetsSubPath = "assets";
        const string nonShippingSubPath = "nonshipping";
        const string shippingSubPath = "shipping";

        // Regular expression used to check that an AssetLocation matches the format of
        // an Azure DevOps Feed. Such feeds look like:
        //      - https://pkgs.dev.azure.com/dnceng/public/_packaging/public-feed-name/nuget/v3/index.json
        //      - https://pkgs.dev.azure.com/dnceng/_packaging/internal-feed-name/nuget/v3/index.json
        public const string AzDoNuGetFeedPattern =
            @"https://pkgs.dev.azure.com/(?<account>[a-zA-Z0-9]+)/(?<visibility>[a-zA-Z0-9-]+/)?_packaging/(?<feed>.+)/nuget/v3/index.json";


        public override async Task<int> ExecuteAsync()
        {
            try
            {
                // Formalize the directory path.
                string rootOutputPath = Path.GetFullPath(_options.OutputDirectory);
                bool success = true;

                // Gather the list of builds that need to be downloaded.
                InputBuilds buildsToDownload = await GatherBuildsToDownloadAsync();

                if (!buildsToDownload.Successful)
                {
                    success = false;
                    if (!_options.ContinueOnError)
                    {
                        return Constants.ErrorCode;
                    }
                }

                Console.WriteLine();

                List<DownloadedBuild> downloadedBuilds = new List<DownloadedBuild>();

                foreach (var build in buildsToDownload.Builds)
                {
                    DownloadedBuild downloadedBuild = await GatherDropForBuildAsync(build, rootOutputPath);
                    if (!downloadedBuild.Successful)
                    {
                        success = false;
                        Console.WriteLine($"Failed to download build with id {build.Id}");
                        if (!_options.ContinueOnError)
                        {
                            return Constants.ErrorCode;
                        }
                    }
                    downloadedBuilds.Add(downloadedBuild);
                }

                // Write the unified drop manifest
                await WriteDropManifest(downloadedBuilds, _options.OutputDirectory);

                // Write the release json
                await WriteReleaseJson(downloadedBuilds, _options.OutputDirectory);

                Console.WriteLine();
                if (!success)
                {
                    Console.WriteLine("One or more failures attempting to download the drop, please see output.");
                    return Constants.ErrorCode;
                }
                else
                {
                    Console.WriteLine("Download successful.");
                    return Constants.SuccessCode;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to gather drop.");
                return Constants.ErrorCode;
            }
        }

        /// <summary>
        ///     Returns the repo uri if it's set explicity or one of the shorthand versions is used.
        /// </summary>
        /// <returns></returns>
        private string GetRepoUri()
        {
            const string sdkUri = "https://github.com/dotnet/core-sdk";
            const string runtimeUri = "https://github.com/dotnet/core-setup";
            const string aspnetUri = "https://github.com/aspnet/AspNetCore";
            string repoUri = _options.RepoUri;

            if (string.IsNullOrEmpty(repoUri))
            {
                if (_options.DownloadSdk)
                {
                    repoUri = sdkUri;
                }
                else if (_options.DownloadRuntime)
                {
                    repoUri = runtimeUri;
                }
                else if (_options.DownloadAspNet)
                {
                    repoUri = aspnetUri;
                }
            }
            return repoUri;
        }

        /// <summary>
        ///     Validate that the root build options are being used
        ///     properly.
        ///     
        ///     This could be partially covered by the SetName attributes, but they aren't
        ///     quite expressive enough for the options
        /// </summary>
        /// <returns>True if they are being used properly, false otherwise.</returns>
        private bool ValidateRootBuildOptions()
        {
            if (_options.RootBuildId != 0)
            {
                if (!string.IsNullOrEmpty(_options.RepoUri) ||
                  !string.IsNullOrEmpty(_options.Channel) ||
                  !string.IsNullOrEmpty(_options.Commit) ||
                  _options.DownloadSdk ||
                  _options.DownloadRuntime ||
                  _options.DownloadAspNet)
                {
                    Console.WriteLine("--id should not be specified with other options.");
                    return false;
                }
                return true;
            }
            else
            {
                // Should specify a repo uri or shorthand, and only one
                if (!(!string.IsNullOrEmpty(_options.RepoUri) ^
                    _options.DownloadSdk ^
                    _options.DownloadRuntime ^
                    _options.DownloadAspNet))
                {
                    Console.WriteLine("Please specify one of --id, --repo, --sdk, --runtime or --aspnet.");
                    return false;
                }
                // Check that commit or channel was specified but not both
                if (!(!string.IsNullOrEmpty(_options.Commit) ^
                    !string.IsNullOrEmpty(_options.Channel)))
                {
                    Console.WriteLine("Please specify either --channel or --commit.");
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        ///     Obtain the root build.
        /// </summary>
        /// <returns>Root build to start with.</returns>
        private async Task<Build> GetRootBuildAsync()
        {
            if (!ValidateRootBuildOptions())
            {
                return null;
            }

            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            string repoUri = GetRepoUri();
            if (_options.RootBuildId != 0)
            {
                Console.WriteLine($"Looking up build by id {_options.RootBuildId}");
                Build rootBuild = await remote.GetBuildAsync(_options.RootBuildId);
                if (rootBuild == null)
                {
                    Console.WriteLine($"No build found with id {_options.RootBuildId}");
                    return null;
                }
                return rootBuild;
            }
            else if (!string.IsNullOrEmpty(repoUri))
            {
                if (!string.IsNullOrEmpty(_options.Channel))
                {
                    IEnumerable<Channel> channels = await remote.GetChannelsAsync();
                    IEnumerable<Channel> desiredChannels = channels.Where(channel => channel.Name.Contains(_options.Channel, StringComparison.OrdinalIgnoreCase));
                    if (desiredChannels.Count() != 1)
                    {
                        Console.WriteLine($"Channel name {_options.Channel} did not match a unique channel. Available channels:");
                        foreach (var channel in channels)
                        {
                            Console.WriteLine($"  {channel.Name}");
                        }
                        return null;
                    }
                    Channel targetChannel = desiredChannels.First();
                    Console.WriteLine($"Looking up latest build of '{repoUri}' on channel '{targetChannel.Name}'");
                    Build rootBuild = await remote.GetLatestBuildAsync(repoUri, targetChannel.Id);
                    if (rootBuild == null)
                    {
                        Console.WriteLine($"No build of '{repoUri}' found on channel '{targetChannel.Name}'");
                        return null;
                    }
                    return rootBuild;
                }
                else if (!string.IsNullOrEmpty(_options.Commit))
                {
                    Console.WriteLine($"Looking up builds of {_options.RepoUri}@{_options.Commit}");
                    IEnumerable<Build> builds = await remote.GetBuildsAsync(_options.RepoUri, _options.Commit);
                    // If more than one is available, print them with their IDs.
                    if (builds.Count() > 1)
                    {
                        Console.WriteLine($"There were {builds.Count()} potential root builds.  Please select one and pass it with --id");
                        foreach (var build in builds)
                        {
                            Console.WriteLine($"  {build.Id}: {build.AzureDevOpsBuildNumber} @ {build.DateProduced.ToLocalTime()}");
                        }
                        return null;
                    }
                    Build rootBuild = builds.SingleOrDefault();
                    if (rootBuild == null)
                    {
                        Console.WriteLine($"No builds were found of {_options.RepoUri}@{_options.Commit}");
                    }
                    return rootBuild;
                }
            }
            // Shouldn't get here if ValidateRootBuildOptions is correct.
            throw new DarcException("Options for root builds were not validated properly. Please contact @dnceng");
        }

        class BuildComparer : IEqualityComparer<Build>
        {
            public bool Equals(Build x, Build y)
            {
                return x.Id == y.Id;
            }

            public int GetHashCode(Build obj)
            {
                return obj.Id;
            }
        }

        /// <summary>
        ///     Given a downloaded build, determine what the product name should be
        ///     for the release json
        /// </summary>
        /// <param name="build">Downloaded build</param>
        /// <returns>Product name</returns>
        private string GetProductNameForReleaseJson(DownloadedBuild build)
        {
            // Preference the github repo name over the azure devops repo name.
            if (!string.IsNullOrEmpty(build.Build.GitHubRepository))
            {
                // Split off the github.com+org name and just use the repo name, all lower case.
                (string owner, string repo) = GitHubClient.ParseRepoUri(build.Build.GitHubRepository);
                return repo.ToLowerInvariant();
            }
            else if (!string.IsNullOrEmpty(build.Build.AzureDevOpsRepository))
            {
                // Use the full repo name without project/account
                (string accountName, string projectName, string repoName) = AzureDevOpsClient.ParseRepoUri(build.Build.AzureDevOpsRepository);
                return repoName.ToLowerInvariant();
            }
            else
            {
                throw new NotImplementedException("Unknown repository name.");
            }
        }

        /// <summary>
        ///     Given a downloaded build, determine what the fileshare location should be.
        /// </summary>
        /// <param name="build">Downloaded build</param>
        /// <returns>File share location</returns>
        public string GetFileShareLocationForReleaseJson(DownloadedBuild build)
        {
            // We only want to have shipping assets in the release json, so append that path
            return Path.Combine(build.OutputDirectory, shippingSubPath);
        }

        /// <summary>
        ///     Write the release json.  Only applicable for separated (ReleaseLayout) drops
        /// </summary>
        /// <param name="downloadedBuilds">List of downloaded builds</param>
        /// <param name="outputDirectory">Output directory write the release json</param>
        /// <returns>Async task</returns>
        private async Task WriteReleaseJson(List<DownloadedBuild> downloadedBuilds, string outputDirectory)
        {
            if (_options.DryRun || !_options.ReleaseLayout)
            {
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "release.json");

            var releaseJson = new[]
            {
                new
                {
                    release = _options.ReleaseName,
                    products = downloadedBuilds
                        .Where(b => b.AnyShippingAssets)
                        .Select(b =>
                            new {
                                name = GetProductNameForReleaseJson(b),
                                fileshare = GetFileShareLocationForReleaseJson(b),
                            }
                        )
                }
            };
            await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(releaseJson, Formatting.Indented));
        }

        /// <summary>
        ///     Write out a manifest of the items in the drop
        /// </summary>
        /// <returns></returns>
        private async Task WriteDropManifest(List<DownloadedBuild> downloadedBuilds, string outputDirectory)
        {
            if (_options.DryRun)
            {
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "manifest.txt");
            if (_options.Overwrite)
            {
                File.Delete(outputPath);
            }
            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                await writer.WriteLineAsync($"Builds:");
                foreach (DownloadedBuild build in downloadedBuilds)
                {
                    await writer.WriteLineAsync($"  - Repo:         {build.Build.GitHubRepository ?? build.Build.AzureDevOpsRepository}");
                    await writer.WriteLineAsync($"    Commit:       {build.Build.Commit}");
                    await writer.WriteLineAsync($"    Branch:       {build.Build.AzureDevOpsBranch}");
                    await writer.WriteLineAsync($"    Produced:     {build.Build.DateProduced}");
                    await writer.WriteLineAsync($"    Build Number: {build.Build.AzureDevOpsBuildNumber}");
                    await writer.WriteLineAsync($"    BAR Build ID: {build.Build.Id}");
                    await writer.WriteLineAsync($"    Assets:");
                    foreach (DownloadedAsset asset in build.DownloadedAssets)
                    {
                        await writer.WriteLineAsync($"      - Name:          {asset.Asset.Name}");
                        await writer.WriteLineAsync($"        Version:       {asset.Asset.Version}");
                        await writer.WriteLineAsync($"        NonShipping:   {asset.Asset.NonShipping}");
                        await writer.WriteLineAsync($"        Source:        {asset.SourceLocation}");
                        await writer.WriteLineAsync($"        Target:        {asset.TargetLocation}");
                        await writer.WriteLineAsync($"        BAR Asset ID:  {asset.Asset.Id}");
                    }
                }
            }
        }

        /// <summary>
        ///     Build the list of builds that will need their assets downloaded.
        /// </summary>
        /// <returns>List of builds to download</returns>
        /// <remarks>
        ///     This can be pretty simple if a full build download is
        ///     not desired (just determine the root build and return it) or it could
        ///     be a matter of determining all builds that contributed to all dependencies.
        /// </remarks>
        private async Task<InputBuilds> GatherBuildsToDownloadAsync()
        {
            Console.WriteLine("Determining what builds to download...");

            // Gather the root build 
            Build rootBuild = await GetRootBuildAsync();
            if (rootBuild == null)
            {
                return new InputBuilds { Successful = false };
            }
            Console.WriteLine($"Root build - Build number {rootBuild.AzureDevOpsBuildNumber} of {rootBuild.AzureDevOpsRepository} @ {rootBuild.Commit}");

            // If transitive (full tree) was not selected, we're done
            if (!_options.Transitive)
            {
                return new InputBuilds()
                {
                    Successful = true,
                    Builds = new List<Build>() { rootBuild }
                };
            }

            HashSet<Build> builds = new HashSet<Build>(new BuildComparer());
            builds.Add(rootBuild);
            IRemoteFactory remoteFactory = new RemoteFactory(_options);
            // Grab dependencies
            IRemote rootBuildRemote = await remoteFactory.GetRemoteAsync(rootBuild.AzureDevOpsRepository, Logger);

            Console.WriteLine($"Getting dependencies of root build...");

            // Flatten for convencience and remove dependencies of types that we don't want if need be.
            if (!_options.IncludeToolset)
            {
                Console.WriteLine("Filtering toolset dependencies from the graph...");
            }

            DependencyGraphBuildOptions buildOptions = new DependencyGraphBuildOptions()
            {
                IncludeToolset = _options.IncludeToolset,
                LookupBuilds = true,
                NodeDiff = NodeDiff.None
            };

            Console.WriteLine("Building graph of all dependencies under root build...");
            DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                remoteFactory,
                rootBuild.GitHubRepository ?? rootBuild.AzureDevOpsRepository,
                rootBuild.Commit,
                buildOptions,
                Logger);

            Dictionary<DependencyDetail, Build> dependencyCache =
                new Dictionary<DependencyDetail, Build>(new DependencyDetailComparer());

            // Cache root build's assets
            foreach (Asset buildAsset in rootBuild.Assets)
            {
                dependencyCache.Add(
                    new DependencyDetail
                    {
                        Name = buildAsset.Name,
                        Version = buildAsset.Version,
                        Commit = rootBuild.Commit,
                    },
                    rootBuild);
            }

            Console.WriteLine($"There are {graph.UniqueDependencies.Count()} unique dependencies in the graph.");
            Console.WriteLine("Full set of builds in graph:");
            foreach (var build in graph.ContributingBuilds)
            {
                Console.WriteLine($"  Build - {build.AzureDevOpsBuildNumber} of {build.GitHubRepository ?? build.AzureDevOpsRepository} @ {build.Commit}");
                builds.Add(build);
            }

            if (graph.DependenciesMissingBuilds.Any())
            {
                Console.WriteLine("Dependencies missing builds:");
                foreach (DependencyDetail dependency in graph.DependenciesMissingBuilds)
                {
                    Console.WriteLine($"  {dependency.Name}@{dependency.Version} @ ({dependency.RepoUri}@{dependency.Commit})");
                }
                if (!_options.ContinueOnError)
                {
                    return new InputBuilds()
                    {
                        Successful = false,
                        Builds = builds
                    };
                }
            }

            return new InputBuilds()
            {
                Successful = true,
                Builds = builds.ToList()
            };
        }

        /// <summary>
        /// Get an asset name that has the version (without it being double included)
        /// </summary>
        /// <param name="asset">Asset</param>
        /// <returns>Name for logging.</returns>
        private string GetAssetNameForLogging(Asset asset)
        {
            string assetNameAndVersion = asset.Name;
            if (!assetNameAndVersion.Contains(asset.Version))
            {
                assetNameAndVersion += $"@{asset.Version}";
            }
            return assetNameAndVersion;
        }

        /// <summary>
        ///     Gather the drop for a specific build.
        /// </summary>
        /// <param name="build">Build to gather drop for</param>
        /// <param name="rootOutputDirectory">Output directory. Must exist.</param>
        private async Task<DownloadedBuild> GatherDropForBuildAsync(Build build, string rootOutputDirectory)
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);
            bool success = true;

            // If the drop is separated, calculate the directory name based on the last element of the build
            // repo uri plus the build number (to disambiguate overlapping builds)
            string outputDirectory = rootOutputDirectory;
            string repoUri = build.GitHubRepository ?? build.AzureDevOpsRepository;
            if (_options.ReleaseLayout)
            {
                int lastSlash = repoUri.LastIndexOf("/");
                if (lastSlash != -1 && lastSlash != repoUri.Length - 1)
                {
                    outputDirectory = Path.Combine(rootOutputDirectory, repoUri.Substring(lastSlash + 1), build.AzureDevOpsBuildNumber);
                }
                else
                {
                    // Might contain invalid path chars, this is currently unhandled.
                    outputDirectory = Path.Combine(rootOutputDirectory, repoUri, build.AzureDevOpsBuildNumber);
                }
                Directory.CreateDirectory(outputDirectory);
            }

            List<DownloadedAsset> downloadedAssets = new List<DownloadedAsset>();
            bool anyShipping = false;

            Console.WriteLine($"Gathering drop for build {build.AzureDevOpsBuildNumber} of {repoUri}");
            using (HttpClient client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
            {
                var assets = await remote.GetAssetsAsync(buildId: build.Id, nonShipping: (!_options.IncludeNonShipping ? (bool?)false : null));
                foreach (var asset in assets)
                {
                    DownloadedAsset downloadedAsset = await DownloadAssetAsync(client, build, asset, outputDirectory);
                    if (downloadedAsset == null)
                    {
                        continue;
                    }
                    else if (!downloadedAsset.Successful)
                    {
                        success = false;
                        if (!_options.ContinueOnError)
                        {
                            Console.WriteLine($"Aborting download.");
                            break;
                        }
                    }
                    else
                    {
                        anyShipping |= !asset.NonShipping;
                        downloadedAssets.Add(downloadedAsset);
                    }
                }
            }

            DownloadedBuild newBuild = new DownloadedBuild
            {
                Successful = success,
                Build = build,
                DownloadedAssets = downloadedAssets,
                OutputDirectory = outputDirectory,
                AnyShippingAssets = anyShipping
            };

            // If separated drop, generate a manifest per build
            if (_options.ReleaseLayout)
            {
                await WriteDropManifest(new List<DownloadedBuild>() { newBuild }, outputDirectory);
            }

            return newBuild;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="asset"></param>
        /// <param name="rootOutputDirectory"></param>
        /// <returns></returns>
        /// <remarks>
        ///     Layout:
        ///     {root dir}\shipping\assets - blobs
        ///     {root dir}\shipping\packages - packages
        ///     {root dir}\nonshipping\assets - blobs
        ///     {root dir}\nonshipping\packages - packages
        /// </remarks>
        private async Task<DownloadedAsset> DownloadAssetAsync(HttpClient client,
                                                                            Build build,
                                                                            Asset asset,
                                                                            string rootOutputDirectory)
        {
            string assetNameAndVersion = GetAssetNameForLogging(asset);
            if (_options.IncludeNonShipping || !asset.NonShipping)
            {
                Console.WriteLine($"  Downloading asset {assetNameAndVersion}");
            }
            else
            {
                Console.WriteLine($"  Skipping non-shipping asset {assetNameAndVersion}");
                return null;
            }

            DownloadedAsset downloadedAsset = new DownloadedAsset()
            {
                Successful = false,
                Asset = asset
            };

            List<string> errors = new List<string>();

            List<AssetLocation> assetLocations = new List<AssetLocation>(asset.Locations);

            if (assetLocations.Count == 0)
            {
                // If there is no location information and the user wants workarounds, add a bunch
                // of feeds.
                if (!_options.NoWorkarounds)
                {
                    if (asset.Name.Contains("/"))
                    {
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetcli.blob.core.windows.net/dotnet/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetclichecksums.blob.core.windows.net/dotnet/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore-tooling/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/aspnet-entityframeworkcore/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/aspnet-extensions/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/dotnet-coreclr/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/dotnet-sdk/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/dotnet-toolset/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.Container, "https://dotnetfeed.blob.core.windows.net/dotnet-windowsdesktop/index.json"));
                    }
                    else
                    {
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/aspnet-aspnetcore-tooling/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/aspnet-entityframeworkcore/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/aspnet-extensions/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/dotnet-coreclr/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/dotnet-sdk/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/dotnet-toolset/index.json"));
                        assetLocations.Add(new AssetLocation(0, LocationType.NugetFeed, "https://dotnetfeed.blob.core.windows.net/dotnet-windowsdesktop/index.json"));
                    }
                }
                else
                {
                    errors.Add($"Asset '{assetNameAndVersion}' has no known location information.");
                }
            }
            else
            {
                if (_options.LatestLocation)
                {
                    downloadedAsset = await DownloadAssetFromLatestLocation(client, build, asset, assetLocations, rootOutputDirectory, errors);
                }
                else
                {
                    downloadedAsset = await DownloadAssetFromAnyLocationAsync(client, build, asset, assetLocations, rootOutputDirectory, errors);
                }

                if (downloadedAsset.Successful)
                {
                    return downloadedAsset;
                }
            }

            // If none of the download attempts succeeded, then we should print out all the error
            // information.
            Console.WriteLine($"    Failed to download asset, errors shown below:");
            foreach (string error in errors)
            {
                Console.WriteLine($"      {error}");
            }

            return downloadedAsset;
        }

        /// <summary>
        /// Download a single asset from any of its locations. Iterate over the asset's locations
        /// until the asset download succeed or all locations have been tested.
        /// </summary>
        private async Task<DownloadedAsset> DownloadAssetFromAnyLocationAsync(HttpClient client,
                                                                            Build build, 
                                                                            Asset asset, 
                                                                            List<AssetLocation> assetLocations, 
                                                                            string rootOutputDirectory, 
                                                                            List<string> errors)
        {
            // Walk the locations and attempt to gather the asset at each one, setting the output
            // path based on the type. Stops at the first successfull download.
            foreach (AssetLocation location in assetLocations)
            {
                var downloadedAsset = await DownloadAssetFromLocation(client,
                    build,
                    asset,
                    location,
                    rootOutputDirectory,
                    errors);

                if (downloadedAsset.Successful)
                {
                    return downloadedAsset;
                }
            }

            return new DownloadedAsset()
            {
                Successful = false,
                Asset = asset
            };
        }

        /// <summary>
        /// Download a single asset from the latest asset location registered.
        /// </summary>
        private async Task<DownloadedAsset> DownloadAssetFromLatestLocation(HttpClient client,
                                                                            Build build,
                                                                            Asset asset,
                                                                            List<AssetLocation> assetLocations,
                                                                            string rootOutputDirectory,
                                                                            List<string> errors)
        {
            AssetLocation latestLocation = assetLocations.OrderByDescending(al => al.Id).First();

            return await DownloadAssetFromLocation(client, 
                build, 
                asset, 
                latestLocation, 
                rootOutputDirectory, 
                errors);
        }

        /// <summary>
        /// Download an asset from the asset location provided.
        /// </summary>
        private async Task<DownloadedAsset> DownloadAssetFromLocation(HttpClient client,
                                                                            Build build,
                                                                            Asset asset,
                                                                            AssetLocation location,
                                                                            string rootOutputDirectory,
                                                                            List<string> errors)
        {
            var subPath = Path.Combine(rootOutputDirectory, asset.NonShipping ? nonShippingSubPath : shippingSubPath);

            var locationType = location.Type;

            // Make sure the location type ends up correct. Currently in some cases,
            // we end up with 'none' or a symbol package ends up with nuget feed.
            // Generally we can make an assumption that if the path doesn't have a
            // '/' then it's a package.  Nuget packages also don't have '.nupkg' suffix
            // (they are just the package name).
            if (!_options.NoWorkarounds)
            {
                if (!asset.Name.Contains("/") && !asset.Name.Contains(".nupkg"))
                {
                    locationType = LocationType.NugetFeed;
                }
                else
                {
                    locationType = LocationType.Container;
                }
            }

            switch (locationType)
            {
                case LocationType.NugetFeed:
                    return await DownloadNugetPackageAsync(client, build, asset, location, subPath, errors);
                case LocationType.Container:
                    return await DownloadBlobAsync(client, build, asset, location, subPath, errors);
                case LocationType.None:
                default:
                    errors.Add($"Unexpected location type {locationType}");
                    break;
            }

            return new DownloadedAsset()
            {
                Successful = false,
                Asset = asset
            };
        }


        /// <summary>
        ///     Download a nuget package.
        /// </summary>
        /// <param name="client">Http client for use in downloading</param>
        /// <param name="asset">Asset to download</param>
        /// <param name="assetLocation">Asset location</param>
        /// <param name="subPath">Root path to download file to.</param>
        /// <returns>True if package could be downloaded, false otherwise.</returns>
        private async Task<DownloadedAsset> DownloadNugetPackageAsync(HttpClient client,
                                                                    Build build,
                                                                    Asset asset,
                                                                    AssetLocation assetLocation,
                                                                    string subPath,
                                                                    List<string> errors)
        {
            // Attempt to figure out how to download this. If the location is a blob storage account, then
            // strip off index.json, append 'flatcontainer', the asset name (lower case), then the version,
            // then {asset name}.{version}.nupkg

            if (IsBlobFeedUrl(assetLocation.Location))
            {
                // Construct the source uri.
                string name = asset.Name.ToLowerInvariant();
                string version = asset.Version.ToLowerInvariant();
                string finalUri = assetLocation.Location.Substring(0, assetLocation.Location.Length - "index.json".Length);
                finalUri += $"flatcontainer/{name}/{version}/{name}.{version}.nupkg";

                // Construct the final path, using the correct casing rather than the blob feed casing.
                string fullTargetPath = Path.Combine(subPath, packagesSubPath, $"{asset.Name}.{asset.Version}.nupkg");
                if (await DownloadFileAsync(client, finalUri, fullTargetPath, errors))
                {
                    return new DownloadedAsset()
                    {
                        Successful = true,
                        Asset = asset,
                        SourceLocation = finalUri,
                        TargetLocation = fullTargetPath
                    };
                }
            }
            else if (IsAzureDevOpsFeedUrl(assetLocation.Location, out var feedAccount, out var feedVisibility, out var feedName))
            {
                string packageContentUrl = $"https://pkgs.dev.azure.com/{feedAccount}/{feedVisibility}_apis/packaging/feeds/{feedName}/nuget/packages/{asset.Name}/versions/{asset.Version}/content";

                string fullTargetPath = Path.Combine(subPath, packagesSubPath, $"{asset.Name}.{asset.Version}.nupkg");

                // feedVisibility == "" means that the feed is internal.
                if (string.IsNullOrEmpty(feedVisibility))
                {
                    if (string.IsNullOrEmpty(_options.AzureDevOpsPat))
                    {
                        LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);
                        _options.AzureDevOpsPat = localSettings.AzureDevOpsToken;
                    }

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", _options.AzureDevOpsPat))));
                }

                if (await DownloadFileAsync(client, packageContentUrl, fullTargetPath, errors))
                {
                    return new DownloadedAsset()
                    {
                        Successful = true,
                        Asset = asset,
                        SourceLocation = packageContentUrl,
                        TargetLocation = fullTargetPath
                    };
                }
            }
            else
            {
                string assetNameAndVersion = GetAssetNameForLogging(asset);
                if (string.IsNullOrEmpty(assetLocation.Location))
                {
                    errors.Add($"Asset location for {assetNameAndVersion} is not available.");
                }
                else
                {
                    errors.Add($"Package uri '{assetLocation.Location} for {assetNameAndVersion} is of an unknown type.");
                }
            }

            return new DownloadedAsset()
            {
                Successful = false,
                Asset = asset
            };
        }

        /// <summary>
        ///     Determine whether this location is a blob feed (sleet) uri.
        /// </summary>
        /// <param name="location">Location</param>
        /// <returns>True if the location is a sleet uri, false otherwise.</returns>
        /// <remarks>
        ///     Blob feed uris look like: https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
        /// </remarks>
        private static bool IsBlobFeedUrl(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
            {
                // Can't parse the location as a URI.  Some other kind of location?
                return false;
            }

            return locationUri.Host.EndsWith("blob.core.windows.net") && location.EndsWith("/index.json");
        }

        /// <summary>
        ///     Determine whether this location is an Azure DevOps Feed URL.
        /// </summary>
        /// <param name="location">Location</param>
        /// <returns>True if the location is an AzDO Feed URL, false otherwise.</returns>
        /// <remarks>
        ///     AzDO feed uris look like: 
        ///         - https://pkgs.dev.azure.com/dnceng/public/_packaging/public-feed-name/nuget/v3/index.json
        ///         - https://pkgs.dev.azure.com/dnceng/_packaging/internal-feed-name/nuget/v3/index.json
        /// </remarks>
        private static bool IsAzureDevOpsFeedUrl(string location, out string feedAccount, out string feedVisibility, out string feedName)
        {
            var parsedUri = Regex.Match(location, AzDoNuGetFeedPattern);

            // If the pattern doesn't match these groups will return an empty string
            feedAccount = parsedUri.Groups["account"].Value;
            feedVisibility = parsedUri.Groups["visibility"].Value;
            feedName = parsedUri.Groups["feed"].Value;

            return parsedUri.Success;
        }

        /// <summary>
        ///     Determine whether this location is an azure devops build url.
        /// </summary>
        /// <param name="location">Location</param>
        /// <returns>True if the location is an Azure Devops uri, false otherwise.</returns>
        /// <remarks>
        ///     Blob feed uris look like: https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
        /// </remarks>
        private static bool IsAzureDevOpsArtifactsUrl(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
            {
                // Can't parse the location as a URI.  Some other kind of location?
                return false;
            }

            return locationUri.Host.Equals("dev.azure.com") && location.EndsWith("/artifacts");
        }

        private async Task<DownloadedAsset> DownloadBlobAsync(HttpClient client,
                                                                Build build,
                                                                Asset asset,
                                                                AssetLocation assetLocation,
                                                                string subPath,
                                                                List<string> errors)
        {
            // Normalize the asset name.  Sometimes the upload to the BAR will have
            // "assets/" prepended to it and sometimes not (depending on the Maestro tasks version).
            // Remove assets/ if it exists so we get consistent target paths.
            string normalizedAssetName = asset.Name;
            if (asset.Name.StartsWith("assets/"))
            {
                normalizedAssetName = asset.Name.Substring("assets/".Length);
            }

            string fullTargetPath = Path.Combine(subPath, assetsSubPath, normalizedAssetName);

            DownloadedAsset downloadedAsset = new DownloadedAsset()
            {
                Successful = false,
                Asset = asset,
                TargetLocation = fullTargetPath
            };
            // If the location is a blob storage account ending in index.json, as would be expected
            // if PushToBlobFeed was used, strip off the index.json and append the asset name. If that doesn't work,
            // prepend "assets/" to the asset name and try that.
            // When uploading assets via the PushToBlobFeed task, assets/ may be prepended (e.g. assets/symbols/)
            // upon upload, but may not be reported to the BAR, or may be reported to BAR.
            // Either way, normalize so that we end up with only one assets/ prepended.

            if (IsBlobFeedUrl(assetLocation.Location))
            {
                string finalBaseUri = assetLocation.Location.Substring(0, assetLocation.Location.Length - "index.json".Length);
                string finalUri1 = $"{finalBaseUri}{asset.Name}";
                string finalUri2 = $"{finalBaseUri}assets/{asset.Name}";
                if (await DownloadFileAsync(client, finalUri1, fullTargetPath, errors))
                {
                    downloadedAsset.Successful = true;
                    downloadedAsset.SourceLocation = finalUri1;
                    return downloadedAsset;
                }
                if (await DownloadFileAsync(client, finalUri2, fullTargetPath, errors))
                {
                    downloadedAsset.Successful = true;
                    downloadedAsset.SourceLocation = finalUri2;
                    return downloadedAsset;
                }
                // Could be under assets/assets/ in some recent builds due to a bug in the release
                // pipeline.
                if (!_options.NoWorkarounds)
                {
                    string finalUri3 = $"{finalBaseUri}assets/assets/{asset.Name}";
                    if (await DownloadFileAsync(client, finalUri3, fullTargetPath, errors))
                    {
                        downloadedAsset.Successful = true;
                        downloadedAsset.SourceLocation = finalUri3;
                        return downloadedAsset;
                    }
                }
                return downloadedAsset;
            }
            else if (IsAzureDevOpsArtifactsUrl(assetLocation.Location))
            {
                // Do not attempt to download from AzDO.
                return downloadedAsset;
            }
            
            if (string.IsNullOrEmpty(assetLocation.Location))
            {
                errors.Add($"Asset location for {asset.Name} is not available.");
            }
            else
            {
                errors.Add($"Blob uri '{assetLocation.Location} for {asset.Name} is of an unknown type");
            }
            return downloadedAsset;
        }

        /// <summary>
        ///     Download a single file and write it to targetFile. For now we just support
        ///     unauthenticated blob storage. Later there could be a version that uses the storage
        ///     SDK.
        /// </summary>
        /// <param name="client">Http client</param>
        /// <param name="sourceUri">Source uri</param>
        /// <param name="targetFile">Target file path. Directories are created.</param>
        /// <returns>Error message if the </returns>
        private async Task<bool> DownloadFileAsync(HttpClient client, string sourceUri, string targetFile, List<string> errors)
        {
            if (_options.DryRun)
            {
                Console.WriteLine($"  {sourceUri} => {targetFile}.");
                return true;
            }

            try
            {
                if (_options.SkipExisting && File.Exists(targetFile))
                {
                    return true;
                }

                string directory = Path.GetDirectoryName(targetFile);
                Directory.CreateDirectory(directory);

                // Ensure the parent target directory has been created.
                using (FileStream outStream = new FileStream(targetFile,
                                                      _options.Overwrite ? FileMode.Create : FileMode.CreateNew,
                                                      FileAccess.Write))
                {
                    using (var inStream = await client.GetStreamAsync(sourceUri))
                    {
                        Console.Write($"  {sourceUri} => {targetFile}...");
                        await inStream.CopyToAsync(outStream);
                        Console.WriteLine("Done");
                    }
                }
                return true;
            }
            catch (IOException e)
            {
                errors.Add($"Failed to write {targetFile}: {e.Message}");
            }
            catch (HttpRequestException e)
            {
                // Ensure we delete the file in this case, otherwise an attempt to download
                // from a separate location will fail.
                File.Delete(targetFile);
                errors.Add($"Failed to download {sourceUri}: {e.Message}");
            }
            return false;
        }
    }
}
