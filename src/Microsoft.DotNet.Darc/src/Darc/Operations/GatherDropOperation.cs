// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        public List<DownloadedAsset> DownloadedAssets { get; set; }
    }

    internal class InputBuilds
    {
        public List<Build> Builds { get; set; }
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
        ///     Returns the repo uri if 
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
                    Build rootBuild = await remote.GetLatestBuildAsync(repoUri, targetChannel.Id.Value);
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
                            Console.WriteLine($"  {build.Id}: {build.AzureDevOpsBuildNumber} @ {build.DateProduced.Value.ToLocalTime()}");
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
                return x.Id.Value == y.Id.Value;
            }

            public int GetHashCode(Build obj)
            {
                return obj.Id.Value;
            }
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
                    await writer.WriteLineAsync($"  - Repo:         {build.Build.AzureDevOpsRepository}");
                    await writer.WriteLineAsync($"    Commit:       {build.Build.Commit}");
                    await writer.WriteLineAsync($"    Branch:       {build.Build.AzureDevOpsBranch}");
                    await writer.WriteLineAsync($"    Produced:     {build.Build.DateProduced.Value}");
                    await writer.WriteLineAsync($"    Build Number: {build.Build.AzureDevOpsBuildNumber}");
                    await writer.WriteLineAsync($"    BAR Build ID: {build.Build.Id}");
                    await writer.WriteLineAsync($"    Assets:");
                    foreach (DownloadedAsset asset in build.DownloadedAssets)
                    {
                        await writer.WriteLineAsync($"      - Name:          {asset.Asset.Name}");
                        await writer.WriteLineAsync($"        Version:       {asset.Asset.Version}");
                        await writer.WriteLineAsync($"        NonShipping:   {asset.Asset.NonShipping.Value}");
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
            List<string> errors = new List<string>();

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
                    Successful = (errors.Count == 0),
                    Builds = new List<Build>() { rootBuild }
                };
            }

            HashSet<Build> builds = new HashSet<Build>(new BuildComparer());
            builds.Add(rootBuild);
            RemoteFactory remoteFactory = new RemoteFactory(_options);
            // Grab dependencies
            IRemote rootBuildRemote = remoteFactory.GetRemote(rootBuild.AzureDevOpsRepository, Logger);

            Console.WriteLine($"Getting dependencies of root build...");

            // Flatten for convencience and remove dependencies of types that we don't want if need be.
            if (!_options.IncludeToolset)
            {
                Console.WriteLine("Filtering toolset dependencies from the graph...");
            }

            Console.WriteLine("Building graph of all dependencies under root build...");
            DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                remoteFactory,
                rootBuild.AzureDevOpsRepository,
                rootBuild.Commit,
                _options.IncludeToolset,
                Logger);

            Dictionary<DependencyDetail, Build> dependencyCache =
                new Dictionary<DependencyDetail, Build>(new DependencyDetailComparer());

            // Cache root build's assets
            foreach (Asset buildAsset in rootBuild.Assets)
            {
                dependencyCache.Add(
                    new DependencyDetail() { Name = buildAsset.Name, Version = buildAsset.Version, Commit = rootBuild.Commit },
                    rootBuild);
            }

            Console.WriteLine($"There are {graph.UniqueDependencies.Count()} unique dependencies in the graph.");
            Console.WriteLine($"Finding builds for all dependencies...");

            // Now go through the list of dependency graphs and look up builds for each one.
            foreach (DependencyDetail dependency in graph.UniqueDependencies)
            {
                Console.WriteLine($"Finding build for {dependency.Name}@{dependency.Version}...");
                Build assetBuild = null;

                // Figure out whether we've seen this dependency before by looking it up in the dependency
                // detail map.
                if (dependencyCache.TryGetValue(dependency, out Build existingBuild))
                {
                    assetBuild = existingBuild;
                }
                else
                {
                    // Go to the BAR to find out where the asset came from.  Every time we look up a new build,
                    // cache its assets in the map.

                    // Look up the asset by name and version.
                    Console.WriteLine($"Looking up {dependency.Name}@{dependency.Version} in Build Asset Registry...");
                    IEnumerable<Asset> matchingAssets = await rootBuildRemote.GetAssetsAsync(dependency.Name, dependency.Version);

                    // Because the same asset could be produced more than once by different builds (e.g. if you had
                    // a stable asset version), look up the builds by ID until we find one that built the right commit.
                    foreach (var asset in matchingAssets)
                    {
                        Console.WriteLine($"Looking up build {asset.BuildId.Value} in Build Asset Registry...");
                        Build potentialBuild = await rootBuildRemote.GetBuildAsync(asset.BuildId.Value);
                        // Do some quick caching after this lookup.
                        foreach (Asset buildAsset in potentialBuild.Assets)
                        {
                            dependencyCache.Add(
                                new DependencyDetail() {
                                    Name = buildAsset.Name,
                                    Version = buildAsset.Version,
                                    Commit = potentialBuild.Commit,
                                    RepoUri = potentialBuild.AzureDevOpsRepository
                                },
                                potentialBuild);
                        }

                        // Determine whether this build matches. Commit should be enough.  We could
                        // also test for repo uri here but I don't think its necessary.
                        if (potentialBuild.Commit == dependency.Commit)
                        {
                            assetBuild = potentialBuild;
                            break;
                        }
                    }
                }

                    
                if (assetBuild == null)
                {
                    errors.Add($"Could not find build that produced {dependency.Name}@{dependency.Version} @ {dependency.Commit}");
                    if (!_options.ContinueOnError)
                    {
                        break;
                    }
                }
                else
                {
                    builds.Add(assetBuild);
                }
            }

            List<Build> buildList = builds.ToList();

            Console.WriteLine("Full set of builds in graph:");
            foreach (var build in buildList)
            {
                Console.WriteLine($"  Build - {build.AzureDevOpsBuildNumber} of {build.AzureDevOpsRepository} @ {build.Commit}");
            }

            if (errors.Any())
            {
                Console.WriteLine($"Failed to obtain full list of builds.  See errors:");
                foreach (string error in errors)
                {
                    Console.WriteLine($" {error}");
                }
            }

            return new InputBuilds()
            {
                Successful = (errors.Count == 0),
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
            string repoUri = build.AzureDevOpsRepository ?? build.GitHubRepository;
            if (_options.Separated)
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

            Console.WriteLine($"Gathering drop for build {build.AzureDevOpsBuildNumber} of {repoUri}");
            using (HttpClient client = new HttpClient())
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
                        downloadedAssets.Add(downloadedAsset);
                    }
                }
            }

            DownloadedBuild newBuild = new DownloadedBuild
            {
                Successful = success,
                Build = build,
                DownloadedAssets = downloadedAssets
            };

            // If separated drop, generate a manifest per build
            if (_options.Separated)
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
            if (_options.IncludeNonShipping || !asset.NonShipping.Value)
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

            if (asset.Locations.Count == 0)
            {
                errors.Add($"Asset '{assetNameAndVersion}' has no known location information.");
            }
            else
            {
                string subPath = Path.Combine(rootOutputDirectory, asset.NonShipping.Value ? nonShippingSubPath : shippingSubPath);

                // Walk the locations and attempt to gather the asset at each one, setting the output
                // path based on the type. Note that if there are multiple locations and their types don't
                // match, consider this an error.
                string locationType = asset.Locations[0].Type;
                foreach (AssetLocation location in asset.Locations)
                {
                    if (locationType != location.Type)
                    {
                        errors.Add($"Asset '{assetNameAndVersion}' has inconsistent location types ({locationType} vs. {location.Type})");
                        break;
                    }

                    switch (locationType)
                    {
                        case "nugetFeed":
                            downloadedAsset = await DownloadNugetPackageAsync(client, build, asset, location, subPath, errors);
                            break;
                        case "container":
                            downloadedAsset = await DownloadBlobAsync(client, build, asset, location, subPath, errors);
                            break;
                        default:
                            errors.Add($"Unexpected location type {locationType}");
                            break;
                    }

                    if (downloadedAsset.Successful)
                    {
                        return downloadedAsset;
                    }
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
            else if (IsMyGetUrl(assetLocation.Location))
            {
                // Construct the download uri.  Make this:
                // https://dotnet.myget.org/F/aspnetcore-dev/api/v3/index.json
                // into this:
                // https://dotnet.myget.org/F/aspnetcore-dev/api/v2/package/AspNetCoreRuntime.3.0.x64/3.0.0-preview-19074-0437
                
                string finalUri = assetLocation.Location.Substring(0, assetLocation.Location.Length - "v3/index.json".Length);
                finalUri += $"v2/package/{asset.Name}/{asset.Version}";
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
        private bool IsBlobFeedUrl(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
            {
                // Can't parse the location as a URI.  Some other kind of location?
                return false;
            }

            return locationUri.Host.EndsWith("blob.core.windows.net") && location.EndsWith("/index.json");
        }

        /// <summary>
        ///     Returns true if the location is a myget url.
        /// </summary>
        /// <param name="location">Location</param>
        /// <returns>True if the location is a myget url, false otherwise.</returns>
        /// <remarks>
        ///     https://dotnet.myget.org/F/aspnetcore-dev/api/v3/index.json
        /// </remarks>
        private bool IsMyGetUrl(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
            {
                // Can't parse the location as a URI.  Some other kind of location?
                return false;
            }

            return locationUri.Host == "dotnet.myget.org" && location.EndsWith("/api/v3/index.json");
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
                return downloadedAsset;
            }
            // WORKAROUND: Right now we don't have the ability to have multiple root build locations
            // So the BAR location gets reported as the overall manifest location.  This isn't correct,
            // but we're stuck with it for now until we redesign how the manifest merging is done.
            // So if we see a myget url here, just look up the asset in the dotnetcli storage account.
            if (IsMyGetUrl(assetLocation.Location) && assetLocation.Location.Contains("aspnetcore-dev"))
            {
                // First try to grab the asset from the dotnetcli storage account
                string dotnetcliStorageUri = $"https://dotnetcli.blob.core.windows.net/dotnet/{asset.Name}";
                if (await DownloadFileAsync(client, $"{dotnetcliStorageUri}", fullTargetPath, errors))
                {
                    downloadedAsset.Successful = true;
                    downloadedAsset.SourceLocation = dotnetcliStorageUri;
                    return downloadedAsset;
                }
                // AspNet symbol packages have incorrect names right now.  They are found on the drop share.
                if (asset.Name.EndsWith(".symbols.nupkg"))
                {
                    string symbolPackageName = asset.Name;
                    int lastSlash = asset.Name.LastIndexOf("/");
                    if (lastSlash != -1)
                    {
                        symbolPackageName = asset.Name.Substring(lastSlash);
                    }
                    string shippingNonShippingFolder = asset.NonShipping.Value ? "NonShipping" : "Shipping";
                    string aspnetciSymbolSharePath = $@"\\aspnetci\drops\AspNetCore\master\{build.AzureDevOpsBuildNumber}\packages\Release\{shippingNonShippingFolder}\{symbolPackageName}";
                    if (await DownloadFromShareAsync(aspnetciSymbolSharePath, fullTargetPath, errors))
                    {
                        downloadedAsset.Successful = true;
                        downloadedAsset.SourceLocation = aspnetciSymbolSharePath;
                        return downloadedAsset;
                    }
                }
                return downloadedAsset;
            }
            else
            {
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
        }

        private async Task<bool> DownloadFromShareAsync(string sourceFile, string targetFile, List<string> errors)
        {
            if (_options.DryRun)
            {
                Console.WriteLine($"  {sourceFile} => {targetFile}.");
                return true;
            }

            try
            {
                string directory = Path.GetDirectoryName(targetFile);
                Directory.CreateDirectory(directory);
                
                // Web client will overwrite, so avoid this if not desired by checking for file existence.
                if (!_options.Overwrite && File.Exists(targetFile))
                {
                    errors.Add($"Failed to write {targetFile}. The file already exists.");
                    return false;
                }

                using (var wc = new WebClient())
                {
                    Uri sourceUri = new Uri(sourceFile);
                    await wc.DownloadFileTaskAsync(sourceUri, targetFile);
                    Console.WriteLine($"  {sourceFile} => {targetFile}.");
                }

                return true;
            }
            catch (Exception e)
            {
                errors.Add($"Failed to write {targetFile}: {e.Message}");
            }

            return false;
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
