// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using BarBuild = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace Microsoft.DotNet.Darc.Operations;

internal class InputBuilds
{
    public IEnumerable<BarBuild> Builds { get; set; }
    public bool Successful { get; set; }
}

internal class GatherDropOperation : Operation
{
    private readonly GatherDropCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly Lazy<TokenCredential> _azureTokenCredential;
    private readonly ILogger<GatherDropOperation> _logger;
    private readonly IRemoteFactory _remoteFactory;

    public GatherDropOperation(
        GatherDropCommandLineOptions options,
        ILogger<GatherDropOperation> logger,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory)
    {
        _options = options;
        _azureTokenCredential = new Lazy<TokenCredential>(AzureAuthentication.GetCliCredential);
        _logger = logger;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
    }

    private const string PackagesSubPath = "packages";
    private const string AssetsSubPath = "assets";
    private const string NonShippingSubPath = "nonshipping";
    private const string ShippingSubPath = "shipping";

    // Regular expression used to check that an AssetLocation matches the format of
    // an Azure DevOps Feed. Such feeds look like:
    //      - https://pkgs.dev.azure.com/dnceng/public/_packaging/public-feed-name/nuget/v3/index.json
    //      - https://pkgs.dev.azure.com/dnceng/_packaging/internal-feed-name/nuget/v3/index.json
    public const string AzDoNuGetFeedPattern =
        @"https://pkgs.dev.azure.com/(?<account>[a-zA-Z0-9]+)/(?<visibility>[a-zA-Z0-9-]+/)?_packaging/(?<feed>.+)/nuget/v3/index.json";
    private const string CDNUri = "ci.dot.net";
    private static readonly List<(string repo, string sha)> DependenciesAlwaysMissingBuilds =
    [
        ("https://github.com/dotnet/corefx", "7ee84596d92e178bce54c986df31ccc52479e772"),
        ("https://github.com/aspnet/xdt", "c01a538851a8ab1a1fbeb2e6243f391fff7587b4")
    ];

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Formalize the directory path.
            var rootOutputPath = Path.GetFullPath(_options.OutputDirectory);
            var success = true;

            // Gather the list of builds that need to be downloaded.
            var rootBuilds = await GetRootBuildsAsync();
            InputBuilds buildsToDownload = await GatherBuildsToDownloadAsync(rootBuilds);

            if (!buildsToDownload.Successful)
            {
                return Constants.ErrorCode;
            }

            Console.WriteLine();

            List<DownloadedBuild> downloadedBuilds = [];
            List<DownloadedAsset> extraDownloadedAssets = [];

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
                if (rootBuilds.Contains(build))
                {
                    try
                    {
                        downloadedBuild.Dependencies = await GetBuildDependenciesAsync(build);
                    }
                    catch (DependencyFileNotFoundException)
                    {
                        // Ignore: this is a repository without a dependencies xml file.
                        // It may be an artificial scenario for a "root" build to have no dependencies.
                    }
                }
                if (downloadedBuild.ExtraDownloadedAssets.Any())
                {
                    Console.WriteLine($"Found {downloadedBuild.ExtraDownloadedAssets.Count()} always-download asset(s) in build {build.Id}:");
                    foreach (var asset in downloadedBuild.ExtraDownloadedAssets)
                    {
                        Console.WriteLine($"   - {asset.Asset.Name}");
                    }
                    extraDownloadedAssets.AddRange(downloadedBuild.ExtraDownloadedAssets);
                }

                downloadedBuilds.Add(downloadedBuild);
            }

            // Write the unified drop manifest
            await WriteDropManifestAsync(downloadedBuilds, extraDownloadedAssets, _options.OutputDirectory);

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
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to gather drop.");
            return Constants.ErrorCode;
        }
    }

    private async Task<IEnumerable<DependencyDetail>> GetBuildDependenciesAsync(BarBuild build)
    {
        var repoUri = build.GetRepository();
        IRemote remote = await _remoteFactory.CreateRemoteAsync(repoUri);
        return await remote.GetDependenciesAsync(repoUri, build.Commit);
    }

    /// <summary>
    ///     Validate that the root build options are being used
    ///     properly.
    ///     
    ///     This could be partially covered by the SetName attributes, but they aren't
    ///     quite expressive enough for the options
    /// </summary>
    /// <returns>True if they are being used properly, false otherwise.</returns>
    private bool ValidateRootBuildsOptions()
    {
        if (_options.RootBuildIds.Any())
        {
            if (!string.IsNullOrEmpty(_options.RepoUri) ||
                !string.IsNullOrEmpty(_options.Channel) ||
                !string.IsNullOrEmpty(_options.Commit))
            {
                Console.WriteLine("--id should not be specified with other options.");
                return false;
            }
            else if (_options.RootBuildIds.Any(id => id == 0))
            {
                Console.WriteLine("0 is not a valid root build id");
                return false;
            }
            return true;
        }
        else
        {
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
    ///     Obtain the root builds to start with.
    /// </summary>
    /// <returns>Root builds to start with, or null if a root build could not be found.</returns>
    private async Task<IEnumerable<BarBuild>> GetRootBuildsAsync()
    {
        if (!ValidateRootBuildsOptions())
        {
            return null;
        }

        string repoUri = _options.RepoUri;

        if (_options.RootBuildIds.Any())
        {
            List<Task<BarBuild>> rootBuildTasks = [];
            foreach (var rootBuildId in _options.RootBuildIds)
            {
                Console.WriteLine($"Looking up build by id {rootBuildId}");
                rootBuildTasks.Add(_barClient.GetBuildAsync(rootBuildId));
            }
            return await Task.WhenAll(rootBuildTasks);
        }
        else if (!string.IsNullOrEmpty(repoUri))
        {
            if (!string.IsNullOrEmpty(_options.Channel))
            {
                IEnumerable<Channel> channels = await _barClient.GetChannelsAsync();
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
                var rootBuild = await _barClient.GetLatestBuildAsync(repoUri, targetChannel.Id);
                if (rootBuild == null)
                {
                    Console.WriteLine($"No build of '{repoUri}' found on channel '{targetChannel.Name}'");
                    return null;
                }
                return [rootBuild];
            }
            else if (!string.IsNullOrEmpty(_options.Commit))
            {
                Console.WriteLine($"Looking up builds of {_options.RepoUri}@{_options.Commit}");
                var builds = await _barClient.GetBuildsAsync(_options.RepoUri, _options.Commit);
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
                BarBuild rootBuild = builds.SingleOrDefault();
                if (rootBuild == null)
                {
                    Console.WriteLine($"No builds were found of {_options.RepoUri}@{_options.Commit}");
                    return null;
                }
                return [rootBuild];
            }
        }
        // Shouldn't get here if ValidateRootBuildOptions is correct.
        throw new DarcException("Options for root builds were not validated properly. Please contact @dnceng");
    }

    private class BuildComparer : IEqualityComparer<BarBuild>
    {
        public bool Equals(BarBuild x, BarBuild y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(BarBuild obj)
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
    private static string GetProductNameForReleaseJson(DownloadedBuild build)
    {
        // Preference the github repo name over the azure devops repo name.
        if (!string.IsNullOrEmpty(build.Build.GitHubRepository))
        {
            // Split off the github.com+org name and just use the repo name, all lower case.
            (_, string repo) = GitHubClient.ParseRepoUri(build.Build.GitHubRepository);
            return repo.ToLowerInvariant();
        }
        else if (!string.IsNullOrEmpty(build.Build.AzureDevOpsRepository))
        {
            // Use the full repo name without project/account
            (_, _, string repoName) = AzureDevOpsClient.ParseRepoUri(build.Build.AzureDevOpsRepository);
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
    public static string GetFileShareLocationForReleaseJson(DownloadedBuild build)
    {
        // We only want to have shipping assets in the release json, so append that path
        return Path.Combine(build.ReleaseLayoutOutputDirectory, ShippingSubPath);
    }

    /// <summary>
    ///     Write the release json.  Only applicable for separated (ReleaseLayout) drops
    /// </summary>
    /// <param name="downloadedBuilds">List of downloaded builds</param>
    /// <param name="outputDirectory">Output directory for the release json</param>
    /// <returns>Async task</returns>
    private async Task WriteReleaseJson(List<DownloadedBuild> downloadedBuilds, string outputDirectory)
    {
        if (_options.DryRun)
        {
            return;
        }

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "release.json");

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
    ///     Write out a manifest of the items in the drop in json format.
    /// </summary>
    /// <returns></returns>
    private async Task WriteDropManifestAsync(List<DownloadedBuild> downloadedBuilds, List<DownloadedAsset> extraAssets, string specificOutputDirectory)
    {
        if (_options.DryRun)
        {
            return;
        }

        var outputPath = Path.Combine(specificOutputDirectory, "manifest.json");

        if (_options.Overwrite)
        {
            File.Delete(outputPath);
        }

        var manifestJson = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds,
            extraAssets,
            _options.OutputDirectory,
            _options.UseRelativePathsInManifest,
            _logger);

        await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(manifestJson, Formatting.Indented));
    }

    /// <summary>
    /// Filter any released builds if the user did not specify --include-released
    /// </summary>
    /// <param name="inputBuilds">Input builds</param>
    /// <returns>Builds to download</returns>
    private IEnumerable<BarBuild> FilterReleasedBuilds(IEnumerable<BarBuild> builds)
    {
        if (!_options.IncludeReleased)
        {
            var releasedBuilds = builds.Where(build => build.Released);
            var nonReleasedBuilds = builds.Where(build => !build.Released);
            foreach (var build in releasedBuilds)
            {
                if (build.Released)
                {
                    Console.WriteLine($"  Skipping download of released build {build.AzureDevOpsBuildNumber} of {build.GetRepository()} @ {build.Commit}");
                }
            }
            return nonReleasedBuilds;
        }

        return builds;
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
    private async Task<InputBuilds> GatherBuildsToDownloadAsync(IEnumerable<BarBuild> rootBuilds)
    {
        Console.WriteLine("Determining what builds to download...");

        if (rootBuilds == null)
        {
            return new InputBuilds { Successful = false };
        }

        foreach (BarBuild rootBuild in rootBuilds)
        {
            Console.WriteLine($"Root build - Build number {rootBuild.AzureDevOpsBuildNumber} of {rootBuild.AzureDevOpsRepository} @ {rootBuild.Commit}");
        }

        // If transitive (full tree) was not selected, we're done
        if (!_options.Transitive)
        {
            var filteredNonTransitiveBuilds = FilterReleasedBuilds(rootBuilds);

            if (!filteredNonTransitiveBuilds.Any())
            {
                Console.WriteLine("All builds were already released.");
                return new InputBuilds()
                {
                    Successful = false
                };
            }

            return new InputBuilds()
            {
                Successful = true,
                Builds = filteredNonTransitiveBuilds
            };
        }

        var builds = new HashSet<BarBuild>(new BuildComparer());
        foreach (BarBuild rootBuild in rootBuilds)
        {
            builds.Add(rootBuild);
        }

        // Flatten for convenience and remove dependencies of types that we don't want if need be.
        if (!_options.IncludeToolset)
        {
            Console.WriteLine("Filtering toolset dependencies from the graph...");
        }

        var buildOptions = new DependencyGraphBuildOptions()
        {
            IncludeToolset = _options.IncludeToolset,
            LookupBuilds = true,
            NodeDiff = NodeDiff.None
        };

        Console.WriteLine("Building graph of all dependencies under root builds...");
        foreach (BarBuild rootBuild in rootBuilds)
        {
            Console.WriteLine($"Building graph for {rootBuild.AzureDevOpsBuildNumber} of {rootBuild.GetRepository()} @ {rootBuild.Commit}");

            var rootBuildRepository = rootBuild.GetRepository();
            DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                _remoteFactory,
                _barClient,
                rootBuildRepository,
                rootBuild.Commit,
                buildOptions,
                _logger);

            // Because the dependency graph starts the build from a repo+sha, it's possible
            // that multiple unique builds of that root repo+sha were done. But we don't want those other builds.
            // So as we walk the full list of contributing builds, filter those that are from rootBuild's repo + sha but not the
            // same build id.

            Console.WriteLine($"There are {graph.UniqueDependencies.Count()} unique dependencies in the graph.");
            Console.WriteLine("Full set of builds in graph:");
            foreach (var build in graph.ContributingBuilds)
            {
                if (build.GetRepository() == rootBuildRepository &&
                    build.Commit == rootBuild.Commit &&
                    build.Id != rootBuild.Id)
                {
                    continue;
                }

                Console.WriteLine($"  Build - {build.AzureDevOpsBuildNumber} of {build.GetRepository()} @ {build.Commit}");
                builds.Add(build);
            }

            // Figure out what is missing
            // This is pretty common actually, and not an error. There are cases where very old versions of specific dependencies
            // are referenced, typically pre-dependency-flow. If we were to have to supply --continue-on-error to get past this,
            // we'd always have it on and would probably miss some real errors. The good news is that this is basically always the
            // same two nodes in the graph; specifically exclude these known missing items.

            var nodesWithNoContributingBuilds = graph.Nodes.Where(node => node.ContributingBuilds.Count == 0 &&
                                                                          !DependenciesAlwaysMissingBuilds.Any(missingNode => node.Repository == missingNode.repo && node.Commit == missingNode.sha)).ToList();
            if (nodesWithNoContributingBuilds.Count != 0)
            {
                Console.WriteLine("Dependency graph nodes missing builds:");
                foreach (var node in nodesWithNoContributingBuilds)
                {
                    Console.WriteLine($"  {node.Repository} @ {node.Commit}");
                }

                if (!_options.ContinueOnError)
                {
                    return new InputBuilds()
                    {
                        Successful = false
                    };
                }
            }
        }

        IEnumerable<BarBuild> filteredBuilds = FilterReleasedBuilds(builds);

        if (!filteredBuilds.Any())
        {
            Console.WriteLine("All builds were already released.");
            return new InputBuilds()
            {
                Successful = false
            };
        }

        return new InputBuilds()
        {
            Successful = true,
            Builds = filteredBuilds
        };
    }

    /// <summary>
    /// Get an asset name that has the version (without it being double-included)
    /// </summary>
    /// <param name="asset">Asset</param>
    /// <returns>Name for logging.</returns>
    private static string GetAssetNameForLogging(Asset asset)
    {
        var assetNameAndVersion = asset.Name;
        if (!assetNameAndVersion.Contains(asset.Version))
        {
            assetNameAndVersion += $"@{asset.Version}";
        }
        return assetNameAndVersion;
    }

    /// <summary>
    /// Gather the drop for a specific build.
    /// </summary>
    /// <param name="build">Build to gather drop for</param>
    /// <param name="rootOutputDirectory">Output directory. Must exist.</param>
    private async Task<DownloadedBuild> GatherDropForBuildAsync(BarBuild build, string rootOutputDirectory)
    {
        var success = true;
        var unifiedOutputDirectory = rootOutputDirectory;
        Directory.CreateDirectory(unifiedOutputDirectory);

        // Calculate the release directory name based on the last element of the build
        // repo uri plus the build number (to disambiguate overlapping builds)
        string releaseOutputDirectory = null;
        var repoUri = build.GetRepository();
        var lastSlash = repoUri.LastIndexOf('/');
        if (lastSlash != -1 && lastSlash != repoUri.Length - 1)
        {
            releaseOutputDirectory = Path.Combine(rootOutputDirectory, repoUri.Substring(lastSlash + 1), build.AzureDevOpsBuildNumber);
        }
        else
        {
            // Might contain invalid path chars, this is currently unhandled.
            releaseOutputDirectory = Path.Combine(rootOutputDirectory, repoUri, build.AzureDevOpsBuildNumber);
        }

        if (_options.Separated)
        {
            Directory.CreateDirectory(releaseOutputDirectory);
        }

        ConcurrentBag<DownloadedAsset> downloadedAssets = [];
        ConcurrentBag<DownloadedAsset> extraDownloadedAssets = [];
        var anyShipping = false;

        Console.WriteLine($"Gathering drop for build {build.AzureDevOpsBuildNumber} of {repoUri}");

        List<Asset> mustDownloadAssets = [];
        string[] alwaysDownloadRegexes = _options.AlwaysDownloadAssetPatterns.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var assets = await _barClient.GetAssetsAsync(buildId: build.Id, nonShipping: (!_options.IncludeNonShipping ? (bool?)false : null));
        if (!string.IsNullOrEmpty(_options.AssetFilter))
        {
            assets = assets.Where(asset => Regex.IsMatch(asset.Name, _options.AssetFilter));
        }

        foreach (string nameMatchRegex in alwaysDownloadRegexes)
        {
            mustDownloadAssets.AddRange(assets.Where(asset => Regex.IsMatch(Path.GetFileName(asset.Name), nameMatchRegex)));
        }

        (bool success, bool anyShipping, ConcurrentBag<DownloadedAsset> downloadedMainAssets) primaryAssetDownloadResult = await DownloadAssetsToDirectories(
            assets,
            releaseOutputDirectory,
            unifiedOutputDirectory);

        success &= primaryAssetDownloadResult.success;
        anyShipping |= primaryAssetDownloadResult.anyShipping;
        downloadedAssets = primaryAssetDownloadResult.downloadedMainAssets;
        if (!success && !_options.ContinueOnError)
        {
            throw new Exception("Failed downloading primary assets; please see logs");
        }

        // Now download the extras (if any)
        if (mustDownloadAssets.Count != 0)
        {
            var extraAssetsDirectory = Path.Join(rootOutputDirectory, "extra-assets");
            Directory.CreateDirectory(extraAssetsDirectory);

            (bool success, bool _, ConcurrentBag<DownloadedAsset> downloadedExtraAssets) extraAssetDownloadResult = await DownloadAssetsToDirectories(
                mustDownloadAssets,
                extraAssetsDirectory,
                unifiedOutputDirectory);

            extraDownloadedAssets = extraAssetDownloadResult.downloadedExtraAssets;
            success &= extraAssetDownloadResult.success;
            if (!success && !_options.ContinueOnError)
            {
                throw new Exception("Failed downloading extra assets; please see logs");
            }
        }

        var newBuild = new DownloadedBuild
        {
            Successful = success,
            Build = build,
            DownloadedAssets = downloadedAssets,
            ExtraDownloadedAssets = extraDownloadedAssets,
            ReleaseLayoutOutputDirectory = releaseOutputDirectory,
            AnyShippingAssets = anyShipping
        };

        if (_options.Separated)
        {
            await WriteDropManifestAsync([newBuild], null, releaseOutputDirectory);
        }

        return newBuild;
    }


    private async Task<(bool success, bool anyShipping, ConcurrentBag<DownloadedAsset> downloadedAssets)> DownloadAssetsToDirectories(
        IEnumerable<Asset> assets,
        string specificAssetDirectory,
        string unifiedOutputDirectory)
    {
        var success = true;
        var downloaded = new ConcurrentBag<DownloadedAsset>();
        var anyShipping = false;
        using (var client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }) { Timeout = TimeSpan.FromMinutes(5) })
        {
            using (var clientThrottle = new SemaphoreSlim(_options.MaxConcurrentDownloads, _options.MaxConcurrentDownloads))
            {

                await Task.WhenAll(assets.Select(asset => Task.Run(async () =>
                {
                    await clientThrottle.WaitAsync();

                    try
                    {
                        DownloadedAsset downloadedAsset = await DownloadAssetAsync(client, asset, specificAssetDirectory, unifiedOutputDirectory);
                        if (downloadedAsset == null)
                        {
                            // Do nothing, decided not to download.
                        }
                        else if (!downloadedAsset.Successful)
                        {
                            success = false;
                            if (!_options.ContinueOnError)
                            {
                                Console.WriteLine($"Aborting download.");
                                return;
                            }
                        }
                        else
                        {
                            anyShipping |= !asset.NonShipping;
                            downloaded.Add(downloadedAsset);
                        }
                    }
                    finally
                    {
                        clientThrottle.Release();
                    }
                })));
            }
        }
        return (success, anyShipping, downloaded);
    }

    /// <summary>
    ///     Download a single asset
    /// </summary>
    /// <param name="client">Http client for use when downloading assets</param>
    /// <param name="asset">Asset to download</param>
    /// <param name="releaseOutputDirectory">Root output directory for the release layout</param>
    /// <param name="unifiedOutputDirectory">Root output directory for the unified layout</param>
    /// <returns></returns>
    /// <remarks>
    ///     Unified Layout:
    ///     {root dir}\shipping\assets - blobs
    ///     {root dir}\shipping\packages - packages
    ///     {root dir}\nonshipping\assets - blobs
    ///     {root dir}\nonshipping\packages - packages
    ///     Release Layout:
    ///     {root dir}\{repo}\{build id}\shipping\assets - blobs
    ///     {root dir}\{repo}\{build id}\shipping\packages - blobs
    ///     {root dir}\{repo}\{build id}\nonshipping\assets - blobs
    ///     {root dir}\{repo}\{build id}\nonshipping\packages - blobs
    /// </remarks>
    private async Task<DownloadedAsset> DownloadAssetAsync(
        HttpClient client,
        Asset asset,
        string releaseOutputDirectory,
        string unifiedOutputDirectory)
    {
        var assetNameAndVersion = GetAssetNameForLogging(asset);
        if (!_options.IncludeNonShipping && asset.NonShipping)
        {
            Console.WriteLine($"  Skipping non-shipping asset {assetNameAndVersion}");
            return null;
        }

        // String builder for the purposes of ensuring that we don't have a lot
        // of interleaved output in the download info.
        var downloadOutput = new StringBuilder();
        downloadOutput.AppendLine($"  Downloading asset {assetNameAndVersion}");

        var downloadedAsset = new DownloadedAsset()
        {
            Successful = false,
            Asset = asset
        };

        List<string> errors = [];

        var assetLocations = new List<AssetLocation>(asset.Locations);

        if (assetLocations.Count == 0)
        {
            // If there is no location information and the user wants workarounds, add a bunch
            // of feeds.
            if (!_options.NoWorkarounds)
            {
                if (asset.Name.Contains('/'))
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
                downloadedAsset = await DownloadAssetFromLatestLocation(client, asset, assetLocations, releaseOutputDirectory, unifiedOutputDirectory, errors, downloadOutput);
            }
            else
            {
                downloadedAsset = await DownloadAssetFromAnyLocationAsync(client, asset, assetLocations, releaseOutputDirectory, unifiedOutputDirectory, errors, downloadOutput);
            }

            if (downloadedAsset.Successful)
            {
                Console.Write(downloadOutput.ToString());
                return downloadedAsset;
            }
        }

        // If none of the download attempts succeeded, then we should print out all the error
        // information.
        downloadOutput.AppendLine($"    Failed to download asset, errors shown below:");
        foreach (var error in errors)
        {
            downloadOutput.AppendLine($"      {error}");
        }
        Console.Write(downloadOutput.ToString());

        return downloadedAsset;
    }

    /// <summary>
    /// Download a single asset from any of its locations. Iterate over the asset's locations
    /// until the asset download succeed or all locations have been tested.
    /// </summary>
    private async Task<DownloadedAsset> DownloadAssetFromAnyLocationAsync(
        HttpClient client,
        Asset asset,
        List<AssetLocation> assetLocations,
        string releaseOutputDirectory,
        string unifiedOutputDirectory,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        // Walk the locations and attempt to gather the asset at each one, setting the output
        // path based on the type. Stops at the first successfull download.
        foreach (AssetLocation location in assetLocations)
        {
            var downloadedAsset = await DownloadAssetFromLocation(
                client,
                asset,
                location,
                releaseOutputDirectory,
                unifiedOutputDirectory,
                errors,
                downloadOutput);

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
    private async Task<DownloadedAsset> DownloadAssetFromLatestLocation(
        HttpClient client,
        Asset asset,
        List<AssetLocation> assetLocations,
        string releaseOutputDirectory,
        string unifiedOutputDirectory,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        AssetLocation latestLocation = assetLocations.OrderByDescending(al => al.Id).First();

        return await DownloadAssetFromLocation(
            client,
            asset,
            latestLocation,
            releaseOutputDirectory,
            unifiedOutputDirectory,
            errors,
            downloadOutput);
    }

    /// <summary>
    /// Download an asset from the asset location provided.
    /// </summary>
    private async Task<DownloadedAsset> DownloadAssetFromLocation(
        HttpClient client,
        Asset asset,
        AssetLocation location,
        string releaseOutputDirectory,
        string unifiedOutputDirectory,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        var releaseSubPath = Path.Combine(releaseOutputDirectory, asset.NonShipping ? NonShippingSubPath : ShippingSubPath);
        var unifiedSubPath = Path.Combine(unifiedOutputDirectory, asset.NonShipping ? NonShippingSubPath : ShippingSubPath);

        var locationType = location.Type;

        // Make sure the location type ends up correct. Currently in some cases,
        // we end up with 'none' or a symbol package ends up with nuget feed.
        // Generally we can make an assumption that if the path doesn't have a
        // '/' then it's a package.  Nuget packages also don't have '.nupkg' suffix
        // (they are just the package name).
        if (!_options.NoWorkarounds)
        {
            if (!asset.Name.Contains('/') && !asset.Name.Contains(".nupkg"))
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
                return await DownloadNugetPackageAsync(client, asset, location, releaseSubPath, unifiedSubPath, errors, downloadOutput);
            case LocationType.Container:
                return await DownloadBlobAsync(client, asset, location, releaseSubPath, unifiedSubPath, errors, downloadOutput);
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
    /// <param name="releaseOutputDirectory">Directory in the release layout to download to</param>
    /// <param name="unifiedOutputDirectory">Directory in the unified layout to download to</param>
    /// <returns>True if package could be downloaded, false otherwise.</returns>
    private async Task<DownloadedAsset> DownloadNugetPackageAsync(
        HttpClient client,
        Asset asset,
        AssetLocation assetLocation,
        string releaseOutputDirectory,
        string unifiedOutputDirectory,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        // Attempt to figure out how to download this. If the location is a blob storage account, then
        // strip off index.json, append 'flatcontainer', the asset name (lower case), then the version,
        // then {asset name}.{version}.nupkg

        var releaseFullSubPath = Path.Combine(releaseOutputDirectory, PackagesSubPath);
        var unifiedFullSubPath = Path.Combine(unifiedOutputDirectory, PackagesSubPath);
        var targetFileName = $"{asset.Name}.{asset.Version}.nupkg";
        // Construct the final path, using the correct casing rather than the blob feed casing.
        var releaseFullTargetPath = Path.Combine(releaseFullSubPath, targetFileName);
        var unifiedFullTargetPath = Path.Combine(unifiedFullSubPath, targetFileName);

        List<string> targetFilePaths = [];

        if (_options.Separated)
        {
            targetFilePaths.Add(releaseFullTargetPath);
        }

        targetFilePaths.Add(unifiedFullTargetPath);

        var downloadedAsset = new DownloadedAsset()
        {
            Successful = false,
            Asset = asset,
            ReleaseLayoutTargetLocation = releaseFullTargetPath,
            UnifiedLayoutTargetLocation = unifiedFullTargetPath,
            LocationType = assetLocation.Type
        };

        if (IsBlobFeedUrl(assetLocation.Location))
        {
            // Construct the source uri.
            var name = asset.Name.ToLowerInvariant();
            var version = asset.Version.ToLowerInvariant();
            var finalUri = GetBlobBaseUri(assetLocation.Location);

            finalUri += $"flatcontainer/{name}/{version}/{name}.{version}.nupkg";

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));
            var cancellationToken = cancellationTokenSource.Token;

            if (await DownloadFileAsync(client, finalUri, null, targetFilePaths, errors, downloadOutput, cancellationToken))
            {
                downloadedAsset.Successful = true;
                downloadedAsset.SourceLocation = finalUri;
                return downloadedAsset;
            }
        }
        else if (IsAzureDevOpsFeedUrl(assetLocation.Location, out var feedAccount, out var feedProject, out var feedName))
        {
            var packageContentUri = await DownloadAssetFromAzureDevOpsFeedAsync(client, asset,
                feedAccount, feedProject, feedName, targetFilePaths, errors, downloadOutput);

            if (packageContentUri != null)
            {
                downloadedAsset.Successful = true;
                downloadedAsset.SourceLocation = packageContentUri;
                return downloadedAsset;
            }
        }
        else
        {
            var assetNameAndVersion = GetAssetNameForLogging(asset);
            if (string.IsNullOrEmpty(assetLocation.Location))
            {
                errors.Add($"Asset location for {assetNameAndVersion} is not available.");
            }
            else
            {
                errors.Add($"Package uri '{assetLocation.Location} for {assetNameAndVersion} is of an unknown type.");
            }
        }

        return downloadedAsset;
    }

    /// <summary>
    /// Download a package from an azure devops nuget feed.
    /// </summary>
    /// <param name="client">Http client to use for the download</param>
    /// <param name="asset">Asset</param>
    /// <param name="feedAccount">Azure devops account</param>
    /// <param name="feedProject">Azure devops project. For organization scoped feeds, this will be empty.</param>
    /// <param name="feedName">Name of feed</param>
    /// <param name="targetFilePaths">Locations that the package should be downloaded to.</param>
    /// <param name="errors">Any errors should be added to this list.</param>
    /// <param name="downloadOutput">Buffer to add download logging info to.</param>
    /// <returns>Package content url if the asset was successfully downloaded, null otherwise.</returns>
    /// <remarks>
    ///     The feed project parameter is interesting here. It can be empty and unused for organization-scoped 
    ///     feeds, meaning their download url is slightly different from a project scoped feed.
    /// </remarks>
    private async Task<string> DownloadAssetFromAzureDevOpsFeedAsync(HttpClient client,
        Asset asset,
        string feedAccount,
        string feedProject,
        string feedName,
        IEnumerable<string> targetFilePaths,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        var assetName = asset.Name;

        // Some blobs get pushed as packages. This is an artifact of a one-off issue in core-sdk
        // see https://github.com/dotnet/arcade/issues/4608 for an overall fix of this.
        // For now, if we get here, ensure that we ask for the package from the right location by
        // stripping off the leading path elements.
        if (!_options.NoWorkarounds)
        {
            assetName = Path.GetFileName(assetName);
        }

        var packageContentUrl = $"https://pkgs.dev.azure.com/{feedAccount}/{feedProject}_apis/packaging/feeds/{feedName}/nuget/packages/{assetName}/versions/{asset.Version}/content";

        var authHeader = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("{0}:{1}", "", _options.AzureDevOpsPat))));

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));
        var cancellationToken = cancellationTokenSource.Token;

        if (await DownloadFileAsync(client, packageContentUrl, authHeader, targetFilePaths, errors, downloadOutput, cancellationToken))
        {
            return packageContentUrl;
        }
        else
        {
            return null;
        }
    }

    /// <summary>
    ///     Determine whether this location is a blob feed (sleet) uri.
    /// </summary>
    /// <param name="location">Location</param>
    /// <returns>True if the location is a sleet uri, false otherwise.</returns>
    /// <remarks>
    ///     Blob feed uris look like: 
    ///         - https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
    ///         - https://ci.dot.net/public
    ///         - https://ci.dot.net/internal
    /// </remarks>
    private static bool IsBlobFeedUrl(string location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out Uri locationUri))
        {
            // Can't parse the location as a URI.  Some other kind of location?
            return false;
        }

        return locationUri.Host.EndsWith("blob.core.windows.net") || 
               locationUri.Host.Equals(CDNUri, StringComparison.OrdinalIgnoreCase);
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
    ///
    ///         - https://pkgs.dev.azure.com/dnceng/internal/_packaging/internal-feed-name2/nuget/v3/index.json
    ///
    ///     Whether or not the "internal/" or "public/" bits of the URI appear corresponds to whether the feed
    ///     is scoped to the organization or scoped to the project.
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
    ///     Gets the base uri of a blob uri
    /// </summary>
    /// <param name="blobUri">Uri of the blob</param>
    /// <returns>The blob uri with index.json removed, ending in a trailing slash</returns>
    /// <remarks>
    ///     Blob feed uris look like: https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json
    /// </remarks>
    private static string GetBlobBaseUri(string blobUri)
    {
        var baseUri = blobUri;

        if (baseUri.EndsWith("index.json"))
        {
            baseUri = baseUri.Substring(0, baseUri.Length - "index.json".Length);
        }

        if (!baseUri.EndsWith('/'))
        {
            baseUri += "/";
        }

        return baseUri;
    }

    private async Task<DownloadedAsset> DownloadBlobAsync(HttpClient client,
        Asset asset,
        AssetLocation assetLocation,
        string releaseOutputDirectory,
        string unifiedOutputDirectory,
        List<string> errors,
        StringBuilder downloadOutput)
    {
        // Normalize the asset name.  Sometimes the upload to the BAR will have
        // "assets/" prepended to it and sometimes not (depending on the Maestro tasks version).
        // Remove assets/ if it exists so we get consistent target paths.
        var normalizedAssetName = asset.Name;
        if (asset.Name.StartsWith("assets/"))
        {
            normalizedAssetName = asset.Name.Substring("assets/".Length);
        }

        var releaseFullSubPath = Path.Combine(releaseOutputDirectory, AssetsSubPath);
        var unifiedFullSubPath = Path.Combine(unifiedOutputDirectory, AssetsSubPath);
        // Construct the final path, using the correct casing rather than the blob feed casing.
        var releaseFullTargetPath = Path.Combine(releaseFullSubPath, normalizedAssetName);
        var unifiedFullTargetPath = Path.Combine(unifiedFullSubPath, normalizedAssetName);

        List<string> targetFilePaths = [];

        if (_options.Separated)
        {
            targetFilePaths.Add(releaseFullTargetPath);
        }

        targetFilePaths.Add(unifiedFullTargetPath);

        var downloadedAsset = new DownloadedAsset()
        {
            Successful = false,
            Asset = asset,
            ReleaseLayoutTargetLocation = releaseFullTargetPath,
            UnifiedLayoutTargetLocation = unifiedFullTargetPath,
            LocationType = assetLocation.Type
        };

        // If the location is a blob storage account ending in index.json, as would be expected
        // if PushToBlobFeed was used, strip off the index.json and append the asset name. If that doesn't work,
        // prepend "assets/" to the asset name and try that.
        // When uploading assets via the PushToBlobFeed task, assets/ may be prepended (e.g. assets/symbols/)
        // upon upload, but may not be reported to the BAR, or may be reported to BAR.
        // Either way, normalize so that we end up with only one assets/ prepended.

        if (IsBlobFeedUrl(assetLocation.Location))
        {
            var finalBaseUri = GetBlobBaseUri(assetLocation.Location);
            var finalUri1 = $"{finalBaseUri}{asset.Name}";
            var finalUri2 = $"{finalBaseUri}assets/{asset.Name}";

            using var cancellationTokenSource1 = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));
            using var cancellationTokenSource2 = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));

            if (await DownloadFileAsync(client, finalUri1, null, targetFilePaths, errors, downloadOutput, cancellationTokenSource1.Token))
            {
                downloadedAsset.Successful = true;
                downloadedAsset.SourceLocation = finalUri1;
                return downloadedAsset;
            }
            if (await DownloadFileAsync(client, finalUri2, null, targetFilePaths, errors, downloadOutput, cancellationTokenSource2.Token))
            {
                downloadedAsset.Successful = true;
                downloadedAsset.SourceLocation = finalUri2;
                return downloadedAsset;
            }

            // Could be under assets/assets/ in some recent builds due to a bug in the release
            // pipeline.
            if (!_options.NoWorkarounds)
            {
                var finalUri3 = $"{finalBaseUri}assets/assets/{asset.Name}";
                using var cancellationTokenSource3 = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));

                if (await DownloadFileAsync(client, finalUri3, null, targetFilePaths, errors, downloadOutput, cancellationTokenSource3.Token))
                {
                    downloadedAsset.Successful = true;
                    downloadedAsset.SourceLocation = finalUri3;
                    return downloadedAsset;
                }

                // Could also not be under /assets, so strip that from the url
                var finalUri4 = finalUri1.Replace("assets/", "", StringComparison.OrdinalIgnoreCase);
                using var cancellationTokenSource4 = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));

                if (await DownloadFileAsync(client, finalUri4, null, targetFilePaths, errors, downloadOutput, cancellationTokenSource4.Token))
                {
                    downloadedAsset.Successful = true;
                    downloadedAsset.SourceLocation = finalUri4;
                    return downloadedAsset;
                }
            }
            return downloadedAsset;
        }
        else if (IsAzureDevOpsFeedUrl(assetLocation.Location, out var feedAccount, out var feedProject, out var feedName))
        {
            // If we are here, it means this is a symbols package or a nupkg published to a feed,
            // remove some known paths from the name

            var replacedName = asset.Name.Replace("assets/", "", StringComparison.OrdinalIgnoreCase);
            replacedName = replacedName.Replace("symbols/", "", StringComparison.OrdinalIgnoreCase);
            replacedName = replacedName.Replace("sdk/", "", StringComparison.OrdinalIgnoreCase);
            replacedName = replacedName.Replace(".symbols", "", StringComparison.OrdinalIgnoreCase);
            replacedName = replacedName.Replace(".nupkg", "", StringComparison.OrdinalIgnoreCase);

            // finally, remove the version from the name, which should be the last element
            var versionSegmentToRemove = $".{asset.Version}";
            if (!replacedName.EndsWith(versionSegmentToRemove))
            {
                _logger.LogWarning($"Warning: Expected that '{asset.Name}', with package and path parts removed, would end in '{asset.Version}'. Instead was '{replacedName}'.");
            }
            replacedName = replacedName.Remove(replacedName.Length - versionSegmentToRemove.Length);

            // create a temp asset with the mangled name and version
            var mangledAsset = new Asset(asset.Id,
                asset.BuildId,
                asset.NonShipping,
                replacedName,
                asset.Version,
                asset.Locations);

            var packageContentUrl = await DownloadAssetFromAzureDevOpsFeedAsync(client,
                mangledAsset,
                feedAccount,
                feedProject,
                feedName,
                targetFilePaths,
                errors,
                downloadOutput);

            if (packageContentUrl != null)
            {
                downloadedAsset.Successful = true;
                downloadedAsset.SourceLocation = packageContentUrl;
            }

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
    ///     Download a single file and write it to targetFile. If suffixes were passed in
    ///     to darc, use those if the unauthenticated download fails.
    /// </summary>
    /// <param name="client">Http client</param>
    /// <param name="sourceUri">Source uri</param>
    /// <param name="targetFilePaths">List of target paths to download to.</param>
    /// <param name="authHeader">Optional authentication header if necessary</param>
    /// <param name="downloadOutput">Console output for the download.</param>
    /// <param name="errors">List of errors. Append error messages to this list if there are failures.</param>
    /// <returns>True if the download succeeded, false otherwise.</returns>
    private async Task<bool> DownloadFileAsync(HttpClient client,
        string sourceUri,
        AuthenticationHeaderValue authHeader,
        IEnumerable<string> targetFilePaths,
        List<string> errors,
        StringBuilder downloadOutput,
        CancellationToken cancellationToken)
    {
        if (_options.DryRun)
        {
            foreach (var targetFile in targetFilePaths)
            {
                downloadOutput.AppendLine($"  {sourceUri} => {targetFile}.");
            }
            return true;
        }

        // Check all existing locations for the target file. If one exists, copy to the others.
        if (_options.SkipExisting)
        {
            var existingFile = targetFilePaths.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(existingFile))
            {
                foreach (var targetFile in targetFilePaths)
                {
                    try
                    {
                        if (!File.Exists(targetFile))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                            File.Copy(existingFile, targetFile);
                        }
                    }
                    catch (IOException e)
                    {
                        errors.Add($"Failed to check/copy for existing {targetFile}: {e.Message}");
                    }
                }
                return true;
            }
        }

        if (await DownloadFileImplAsync(client, sourceUri, authHeader, targetFilePaths, errors, downloadOutput, cancellationToken))
        {
            return true;
        }
        else if (!_options.UseAzureCredentialForBlobs)
        {
            // Append and attempt to use the suffixes that were passed in to download from the uri
            foreach (string sasSuffix in _options.SASSuffixes)
            {
                if (await DownloadFileImplAsync(client, $"{sourceUri}{sasSuffix}", authHeader, targetFilePaths, errors, downloadOutput, cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///     Download a single file and write it to targetFile.
    /// </summary>
    /// <param name="client">Http client</param>
    /// <param name="sourceUri">Source uri</param>
    /// <param name="authHeader">Optional authentication header if necessary</param>
    /// <param name="targetFile">Target file path. Directories are created.</param>
    /// <param name="errors">List of errors. Append error messages to this list if there are failures.</param>
    /// <param name="downloadOutput">Console output for the download.</param>
    /// <returns>True if the download succeeded, false otherwise.</returns>
    private async Task<bool> DownloadFileImplAsync(HttpClient client,
        string sourceUri,
        AuthenticationHeaderValue authHeader,
        IEnumerable<string> targetFiles,
        List<string> errors,
        StringBuilder downloadOutput,
        CancellationToken cancellationToken)
    {
        // Use a temporary in progress file name so we don't end up with corrupted
        // half downloaded files. Use the first location as the
        var temporaryFileName = $"{targetFiles.First()}.inProgress";

        try
        {
            await DeleteFileWithRetryAsync(temporaryFileName).ConfigureAwait(false);

            foreach (var targetFile in targetFiles)
            {
                var directory = Path.GetDirectoryName(targetFile);
                Directory.CreateDirectory(directory);
            }

            // Ensure the parent target directory has been created.
            using (var outStream = new FileStream(temporaryFileName,
                       _options.Overwrite ? FileMode.Create : FileMode.CreateNew,
                       FileAccess.Write))
            {
                var manager = new HttpRequestManager(
                    client,
                    HttpMethod.Get,
                    sourceUri,
                    _logger,
                    authHeader: authHeader,
                    configureRequestMessage: ConfigureRequestMessage,
                    httpCompletionOption: HttpCompletionOption.ResponseHeadersRead);

                using (var response = await manager.ExecuteAsync())
                {
                    using (var inStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    {
                        downloadOutput.AppendLine($"    {sourceUri} =>");
                        foreach (var targetFile in targetFiles)
                        {
                            downloadOutput.AppendLine($"      {targetFile}");
                        }
                        await inStream.CopyToAsync(outStream, cancellationToken);
                    }
                }
            }

            foreach (var targetFile in targetFiles)
            {
                // Rename file to the target file name.
                File.Copy(temporaryFileName, targetFile);
            }

            return true;
        }
        catch (IOException e)
        {
            errors.Add($"Failed to write output file: {e.Message}");
        }
        catch (HttpRequestException e)
        {
            foreach (var targetFile in targetFiles)
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }
            }
            errors.Add($"Failed to download {sourceUri}: {e.Message}");
        }
        catch (OperationCanceledException e)
        {
            errors.Add($"While trying to download from {sourceUri} the download operation was cancelled: {e.Message}");
        }
        finally
        {
            try
            {
                await DeleteFileWithRetryAsync(temporaryFileName).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                errors.Add($"Failed to delete {temporaryFileName}: {e.Message}");
            }
        }
        return false;
    }

    private void ConfigureRequestMessage(HttpRequestMessage request)
    {
        if (request.RequestUri.Host.Contains(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
            request.RequestUri.Host.Equals(CDNUri, StringComparison.OrdinalIgnoreCase))
        {
            // add API version to support Bearer token authentication
            request.Headers.Add("x-ms-version", "2023-08-03");

            if (_options.UseAzureCredentialForBlobs)
            {
                var tokenRequest = new TokenRequestContext(["https://storage.azure.com/"]);
                var token = _azureTokenCredential.Value.GetToken(tokenRequest, CancellationToken.None);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }
        }
    }

    /// <summary>
    /// Delete a file with retry. Sometimes when gathering a drop directly to a share
    /// we can get occasional deletion failures. All of them seen so far are UnauthorizedAccessExceptions
    /// </summary>
    /// <param name="filePath">Full path to the file to delete.</param>
    private static async Task DeleteFileWithRetryAsync(string filePath)
    {
        await ExponentialRetry.Default.RetryAsync(
            () =>
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            },
            ex => Console.WriteLine($"Failed to delete {filePath}: {ex.Message}"),
            ex => ex is UnauthorizedAccessException);
    }
}
