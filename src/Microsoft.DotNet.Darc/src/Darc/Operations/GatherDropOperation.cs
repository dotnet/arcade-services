// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
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

        private static readonly List<(string repo, string sha)> DependenciesAlwaysMissingBuilds = new List<(string, string)>()
        {
            ("https://github.com/dotnet/corefx", "7ee84596d92e178bce54c986df31ccc52479e772"),
            ("https://github.com/aspnet/xdt", "c01a538851a8ab1a1fbeb2e6243f391fff7587b4")
        };

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                // Formalize the directory path.
                string rootOutputPath = Path.GetFullPath(_options.OutputDirectory);
                bool success = true;

                // Gather the list of builds that need to be downloaded.
                var rootBuilds = await GetRootBuildsAsync();
                InputBuilds buildsToDownload = await GatherBuildsToDownloadAsync(rootBuilds);

                if (!buildsToDownload.Successful)
                {
                    return Constants.ErrorCode;
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

                    downloadedBuilds.Add(downloadedBuild);
                }

                // Write the unified drop manifest
                await WriteDropManifestAsync(downloadedBuilds, _options.OutputDirectory);

                // Write the release json
                await WriteReleaseJson(downloadedBuilds, _options.OutputDirectory);

                // Create the release nupkg layout
                string nugetPublishFilesReleaseLocation = Path.Combine(_options.OutputDirectory, "publish_files", "nuget");
                CreateReleasePackageLayout(downloadedBuilds, nugetPublishFilesReleaseLocation);

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
                Logger.LogError(e, "Error: Failed to gather drop.");
                return Constants.ErrorCode;
            }
        }

        private async Task<IEnumerable<DependencyDetail>> GetBuildDependenciesAsync(Build build)
        {
            string repoUri = string.IsNullOrEmpty(build.GitHubRepository) ? build.AzureDevOpsRepository : build.GitHubRepository;
            IRemote remote = RemoteFactory.GetRemote(_options, repoUri, Logger);
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
        private async Task<IEnumerable<Build>> GetRootBuildsAsync()
        {
            if (!ValidateRootBuildsOptions())
            {
                return null;
            }

            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            string repoUri = _options.RepoUri;

            if (_options.RootBuildIds.Any())
            {
                List<Task<Build>> rootBuildTasks = new List<Task<Build>>();
                foreach (var rootBuildId in _options.RootBuildIds)
                {
                    Console.WriteLine($"Looking up build by id {rootBuildId}");
                    rootBuildTasks.Add(remote.GetBuildAsync(rootBuildId));
                }
                return await Task.WhenAll(rootBuildTasks);
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
                    return new List<Build> { rootBuild };
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
                        return null;
                    }
                    return new List<Build> { rootBuild };
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
            return Path.Combine(build.ReleaseLayoutOutputDirectory, shippingSubPath);
        }

        private const string githubRepoPrefix = "https://github.com/";
        private const string azdoRepoPrefix = "https://dev.azure.com/dnceng/internal/_git/";
        private const string coreRepoCategory = "core";
        private const string aspnetCategory = "aspnet";
        private const string wcfCategory = "wcf";
        
        // The following is the list of repos that should be picked up by the tooling
        // This list is effectively static, but not the full set of repos that are in the graph,
        // as our release process does not publish all of the packages (e.g. not nuget.client).
        // Each dictionary entry maps to a tuple of the category of the package (owner) and short name of the repo.
        private static readonly Dictionary<string, (string, string)> repositories = new Dictionary<string, (string, string)>()
        {
            // Core

            // Public
            { $"{githubRepoPrefix}dotnet/corefx", (coreRepoCategory, "corefx") },
            { $"{githubRepoPrefix}dotnet/coreclr", (coreRepoCategory, "coreclr") },
            { $"{githubRepoPrefix}dotnet/core-setup", (coreRepoCategory, "core-setup") },
            { $"{githubRepoPrefix}dotnet/runtime", (coreRepoCategory, "runtime") },
            { $"{githubRepoPrefix}dotnet/windowsdesktop", (coreRepoCategory, "windowsdesktop") },
            { $"{githubRepoPrefix}dotnet/templating", (coreRepoCategory, "templating") },
            // Internal
            { $"{azdoRepoPrefix}dotnet-corefx", (coreRepoCategory, "corefx") },
            { $"{azdoRepoPrefix}dotnet-coreclr", (coreRepoCategory, "coreclr") },
            { $"{azdoRepoPrefix}dotnet-core-setup", (coreRepoCategory, "core-setup") },
            { $"{azdoRepoPrefix}dotnet-runtime", (coreRepoCategory, "runtime") },
            { $"{azdoRepoPrefix}dotnet-windowsdesktop", (coreRepoCategory, "windowsdesktop") },
            { $"{azdoRepoPrefix}dotnet-templating", (coreRepoCategory, "templating") },

            // ASPNET

            // Public
            { $"{githubRepoPrefix}dotnet/extensions", (aspnetCategory, "extensions") },
            { $"{githubRepoPrefix}dotnet/aspnetcore", (aspnetCategory, "aspnetcore") },
            { $"{githubRepoPrefix}dotnet/aspnetcore-tooling", (aspnetCategory, "aspnetcore-tooling") },
            { $"{githubRepoPrefix}dotnet/efcore", (aspnetCategory, "efcore") },
            { $"{githubRepoPrefix}dotnet/ef6", (aspnetCategory, "ef6") },
            { $"{githubRepoPrefix}dotnet/blazor", (aspnetCategory, "blazor") },
            // Internal
            { $"{azdoRepoPrefix}dotnet-extensions", (aspnetCategory, "extensions") },
            { $"{azdoRepoPrefix}dotnet-aspnetcore", (aspnetCategory, "aspnetcore") },
            { $"{azdoRepoPrefix}dotnet-aspnetcore-tooling", (aspnetCategory, "aspnetcore-tooling") },
            { $"{azdoRepoPrefix}dotnet-efcore", (aspnetCategory, "efcore") },
            { $"{azdoRepoPrefix}dotnet-ef6", (aspnetCategory, "ef6") },
            { $"{azdoRepoPrefix}dotnet-blazor", (aspnetCategory, "blazor") },

            // WCF

            // Public
            { $"{githubRepoPrefix}dotnet/wcf", (wcfCategory, "wcf") },
            // Internal
            { $"{azdoRepoPrefix}dotnet-wcf", (wcfCategory, "wcf") },
        };

        private static readonly List<string> doNotListSymbolPackageFilenamePrefixes = new List<string>()
        {
            // Do not include targeting pack symbol packages: https://github.com/dotnet/core-setup/issues/8310
            // They shouldn't have PDBs anyway, but due to tool behavior plus an artifact issue,
            // they do have PDB and publishing them breaks PDB conversion later on.
            "Microsoft.NETCore.App.Ref.",
            "Microsoft.NETCore.App.Internal.",
            "Microsoft.WindowsDesktop.App.Ref."
        };

        private const string identitiesDirectoryName = "identities";
        private const string nupkgDirectoryName = "nupkgs";
        private const string sympkgsDirectoryName = "sympkgs";
        private const string allNupkgsFileName = "nupkgs-all-just-for-reference.txt";
        private const string nupkgsFileNamePrefix = "nupkgs-";
        private const string sympkgsFileName = "sympkgs-all.txt";
        private const string packagesSubDir = "packages";
        private const string symPackagesSubDir = "assets/symbols";

        /// <summary>
        ///     Create the nupkg layout required for the final release.
        /// </summary>
        /// <param name="downloadedBuilds">List of downloaded builds</param>
        /// <param name="outputDirectory">Output directory to create the nupkg layout</param>
        /// <remarks>
        ///     Generates the nupkg layout that used by the release pipeline. The layout is as follows:
        ///     identities\
        ///         csv files containing package name + version, named with short name of repo
        ///     nupkgs\
        ///         repo-shortname\
        ///             packages\
        ///                 shipping packages from repo
        ///     sympkgs\
        ///         repo-shortname\
        ///             assets\
        ///                 symbols\
        ///                     symbol packages from repo
        ///     nupkgs-all-just-for-reference.txt - file list of all packages, 
        ///     nupkgs-aspnet.txt - file list of all packages that aspnetcore owns on nuget.org
        ///     nupkgs-core.txt - file list of all packages that dotnet core owns on nuget.org
        ///     sympkgs-all.txt - file list of all symbol packages
        /// </remarks>
        private void CreateReleasePackageLayout(List<DownloadedBuild> downloadedBuilds, string outputDirectory)
        {
            if (_options.DryRun)
            {
                return;
            }

            // Identify the subdirectories. Do not need to create these now because
            // we will create them as they go
            string nupkgDirectory = Path.Combine(outputDirectory, nupkgDirectoryName);
            string sympkgsDirectory = Path.Combine(outputDirectory, sympkgsDirectoryName);

            // Universal text file content
            StringBuilder allNupkgsFileContent = new StringBuilder();
            StringBuilder sympkgsFileContent = new StringBuilder();

            // Identity Csv contents
            Dictionary<string, StringBuilder> identitiesFileContents = new Dictionary<string, StringBuilder>();

            // core/wcf/aspnet nupkg lists
            Dictionary<string, StringBuilder> nupkgFileContents = new Dictionary<string, StringBuilder>();

            // Walk each input repo build and start filling out the directories
            foreach (var downloadedBuild in downloadedBuilds)
            {
                // Start by determining whether we need to look at this build at all.
                string repository = downloadedBuild.Build.GitHubRepository ?? downloadedBuild.Build.AzureDevOpsRepository;

                if (!repositories.TryGetValue(repository, out (string, string) categoryAndShortName))
                {
                    continue;
                }

                (string category, string shortName) = categoryAndShortName;

                // This goes into the release layout. Walk the downloaded assets
                foreach (DownloadedAsset asset in downloadedBuild.DownloadedAssets)
                {
                    // If the asset is a shipping package, it goes into nupkgDirectory\<short name>\packages    
                    if (!asset.Asset.NonShipping && asset.LocationType == LocationType.NugetFeed)
                    {
                        // Create the target directory
                        string targetFile = Path.Combine(nupkgDirectory, shortName,
                            packagesSubDir, Path.GetFileName(asset.ReleaseLayoutTargetLocation));
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile));

                        File.Copy(asset.ReleaseLayoutTargetLocation, targetFile, true);

                        // Add the relative path to the various spots. Choose paths are relative to the root output dir
                        string relativePackagePath = Path.GetRelativePath(outputDirectory, targetFile);
                        allNupkgsFileContent.AppendLine(relativePackagePath);

                        StringBuilder categoryStringBuilder = nupkgFileContents.GetOrAddValue(category,
                            () => new StringBuilder());
                        categoryStringBuilder.AppendLine(relativePackagePath);

                        // Do the same for the identity package. It gets a "name,version" (no file extension).
                        // For the identities, the categories are by repo short name
                        StringBuilder identityStringBuilder = identitiesFileContents.GetOrAddValue(shortName,
                            () => new StringBuilder());
                        identityStringBuilder.AppendLine($"{asset.Asset.Name},{asset.Asset.Version}");
                    }
                    // Otherwise, if the asset is a symbol package (ends in .symbols.nupkg), then copy it to symbols
                    else if (asset.Asset.Name.EndsWith(".symbols.nupkg"))
                    {
                        if (doNotListSymbolPackageFilenamePrefixes.Any(doNotListPrefix => 
                            Path.GetFileName(asset.ReleaseLayoutTargetLocation).StartsWith(doNotListPrefix)))
                        {
                            continue;
                        }

                        // Create the target directory
                        string targetFile = Path.Combine(sympkgsDirectory, shortName,
                            symPackagesSubDir, Path.GetFileName(asset.ReleaseLayoutTargetLocation));
                        Directory.CreateDirectory(Path.GetDirectoryName(targetFile));

                        File.Copy(asset.ReleaseLayoutTargetLocation, targetFile, true);

                        // Add the relative path to the sympkg list. Choose paths are relative to the root output dir
                        string relativeSymPackagePath = Path.GetRelativePath(outputDirectory, targetFile);
                        sympkgsFileContent.AppendLine(relativeSymPackagePath);
                    }
                }
            }

            Directory.CreateDirectory(outputDirectory);
            
            File.WriteAllText(Path.Combine(outputDirectory, allNupkgsFileName), allNupkgsFileContent.ToString());
            File.WriteAllText(Path.Combine(outputDirectory, sympkgsFileName), sympkgsFileContent.ToString());
                
            // Write out for each category
            foreach (KeyValuePair<string, StringBuilder> nupkgsByCategory in nupkgFileContents)
            {
                File.WriteAllText(Path.Combine(outputDirectory, $"{nupkgsFileNamePrefix}{nupkgsByCategory.Key}.txt"),
                    nupkgsByCategory.Value.ToString());
            }

            // Write out the identities
            string identitiesDirectory = Path.Combine(outputDirectory, identitiesDirectoryName);
            Directory.CreateDirectory(identitiesDirectory);
            foreach (KeyValuePair<string, StringBuilder> identityByCategory in identitiesFileContents)
            {
                File.WriteAllText(Path.Combine(identitiesDirectory, $"{identityByCategory.Key}.csv"),
                    identityByCategory.Value.ToString());
            }
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
        ///     Write out a manifest of the items in the drop in json format.
        /// </summary>
        /// <returns></returns>
        private async Task WriteDropManifestAsync(List<DownloadedBuild> downloadedBuilds, string specificOutputDirectory)
        {
            if (_options.DryRun)
            {
                return;
            }

            string outputPath = Path.Combine(specificOutputDirectory, "manifest.json");

            if (_options.Overwrite)
            {
                File.Delete(outputPath);
            }

            var manifestJson = ManifestHelper.GenerateDarcAssetJsonManifest(downloadedBuilds, 
                                                                            _options.OutputDirectory,
                                                                            _options.UseRelativePathsInManifest);

            await File.WriteAllTextAsync(outputPath, JsonConvert.SerializeObject(manifestJson, Formatting.Indented));
        }

        /// <summary>
        /// Filter any released builds if the user did not specify --include-released
        /// </summary>
        /// <param name="inputBuilds">Input builds</param>
        /// <returns>Builds to download</returns>
        private IEnumerable<Build> FilterReleasedBuilds(IEnumerable<Build> builds)
        {
            if (!_options.IncludeReleased)
            {
                var releasedBuilds = builds.Where(build => build.Released);
                var nonReleasedBuilds = builds.Where(build => !build.Released);
                foreach (var build in releasedBuilds)
                {
                    if (build.Released)
                    {
                        Console.WriteLine($"  Skipping download of released build {build.AzureDevOpsBuildNumber} of {build.GitHubRepository ?? build.AzureDevOpsRepository} @ {build.Commit}");
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
        private async Task<InputBuilds> GatherBuildsToDownloadAsync(IEnumerable<Build> rootBuilds)
        {
            Console.WriteLine("Determining what builds to download...");

            if (rootBuilds == null)
            {
                return new InputBuilds { Successful = false };
            }

            foreach (Build rootBuild in rootBuilds)
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

            HashSet<Build> builds = new HashSet<Build>(new BuildComparer());
            foreach (Build rootBuild in rootBuilds)
            {
                builds.Add(rootBuild);
            }
            IRemoteFactory remoteFactory = new RemoteFactory(_options);

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

            Console.WriteLine("Building graph of all dependencies under root builds...");
            foreach (Build rootBuild in rootBuilds)
            {
                Console.WriteLine($"Building graph for {rootBuild.AzureDevOpsBuildNumber} of {rootBuild.GitHubRepository ?? rootBuild.AzureDevOpsRepository} @ {rootBuild.Commit}");

                string rootBuildRepository = rootBuild.GitHubRepository ?? rootBuild.AzureDevOpsRepository;
                DependencyGraph graph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                    remoteFactory,
                    rootBuildRepository,
                    rootBuild.Commit,
                    buildOptions,
                    Logger);

                // Because the dependency graph starts the build from a repo+sha, it's possible
                // that multiple unique builds of that root repo+sha were done. But we don't want those other builds.
                // So as we walk the full list of contributing builds, filter those that are from rootBuild's repo + sha but not the
                // same build id.

                Console.WriteLine($"There are {graph.UniqueDependencies.Count()} unique dependencies in the graph.");
                Console.WriteLine("Full set of builds in graph:");
                foreach (var build in graph.ContributingBuilds)
                {
                    if ((build.GitHubRepository ?? build.AzureDevOpsRepository) == rootBuildRepository && 
                        build.Commit == rootBuild.Commit &&
                        build.Id != rootBuild.Id)
                    {
                        continue;
                    }

                    Console.WriteLine($"  Build - {build.AzureDevOpsBuildNumber} of {build.GitHubRepository ?? build.AzureDevOpsRepository} @ {build.Commit}");
                    builds.Add(build);
                }

                // Figure out what is missing
                // This is pretty common actually, and not an error. There are cases where very old versions of specific dependencies
                // are referenced, typically pre-dependency flow. If we were to have to supply --continue-on-error to get past this,
                // we'd always have it on and would probably miss some real errors. The good news is that this is basically always the
                // same two nodes in the graph. Specifically exclude these known missing items

                var nodesWithNoContributingBuilds = graph.Nodes.Where(node => !node.ContributingBuilds.Any() && 
                    !DependenciesAlwaysMissingBuilds.Any(missingNode => node.Repository == missingNode.repo && node.Commit == missingNode.sha)).ToList();
                if (nodesWithNoContributingBuilds.Any())
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

            IEnumerable<Build> filteredBuilds = FilterReleasedBuilds(builds);

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

            string unifiedOutputDirectory = rootOutputDirectory;
            Directory.CreateDirectory(unifiedOutputDirectory);

            // Calculate the release directory name based on the last element of the build
            // repo uri plus the build number (to disambiguate overlapping builds)
            string releaseOutputDirectory = null;
            string repoUri = build.GitHubRepository ?? build.AzureDevOpsRepository;
            int lastSlash = repoUri.LastIndexOf("/");
            if (lastSlash != -1 && lastSlash != repoUri.Length - 1)
            {
                releaseOutputDirectory = Path.Combine(rootOutputDirectory, repoUri.Substring(lastSlash + 1), build.AzureDevOpsBuildNumber);
            }
            else
            {
                // Might contain invalid path chars, this is currently unhandled.
                releaseOutputDirectory = Path.Combine(rootOutputDirectory, repoUri, build.AzureDevOpsBuildNumber);
            }
            Directory.CreateDirectory(releaseOutputDirectory);

            ConcurrentBag<DownloadedAsset> downloadedAssets = new ConcurrentBag<DownloadedAsset>();
            bool anyShipping = false;

            Console.WriteLine($"Gathering drop for build {build.AzureDevOpsBuildNumber} of {repoUri}");
            using (HttpClient client = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true }))
            {
                var assets = await remote.GetAssetsAsync(buildId: build.Id, nonShipping: (!_options.IncludeNonShipping ? (bool?)false : null));
                if (!string.IsNullOrEmpty(_options.AssetFilter))
                {
                    assets = assets.Where(asset => Regex.IsMatch(asset.Name, _options.AssetFilter));
                }

                using (var clientThrottle = new SemaphoreSlim(_options.MaxConcurrentDownloads, _options.MaxConcurrentDownloads))
                {
                    await Task.WhenAll(assets.Select(async asset =>
                    {
                        await clientThrottle.WaitAsync();

                        try
                        {
                            DownloadedAsset downloadedAsset = await DownloadAssetAsync(client, build, asset, releaseOutputDirectory, unifiedOutputDirectory);
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
                                downloadedAssets.Add(downloadedAsset);
                            }

                        }
                        finally
                        {
                            clientThrottle.Release();
                        }
                    }));
                }
            }

            DownloadedBuild newBuild = new DownloadedBuild
            {
                Successful = success,
                Build = build,
                DownloadedAssets = downloadedAssets,
                ReleaseLayoutOutputDirectory = releaseOutputDirectory,
                AnyShippingAssets = anyShipping
            };

            await WriteDropManifestAsync(new List<DownloadedBuild>() { newBuild }, releaseOutputDirectory);

            return newBuild;
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
        private async Task<DownloadedAsset> DownloadAssetAsync(HttpClient client,
                                                                Build build,
                                                                Asset asset,
                                                                string releaseOutputDirectory,
                                                                string unifiedOutputDirectory)
        {
            string assetNameAndVersion = GetAssetNameForLogging(asset);
            if (!_options.IncludeNonShipping && asset.NonShipping)
            {
                Console.WriteLine($"  Skipping non-shipping asset {assetNameAndVersion}");
                return null;
            }

            // String builder for the purposes of ensuring that we don't have a lot
            // of interleaved output in the download info.
            StringBuilder downloadOutput = new StringBuilder();
            downloadOutput.AppendLine($"  Downloading asset {assetNameAndVersion}");

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
                    downloadedAsset = await DownloadAssetFromLatestLocation(client, build, asset, assetLocations, releaseOutputDirectory, unifiedOutputDirectory, errors, downloadOutput);
                }
                else
                {
                    downloadedAsset = await DownloadAssetFromAnyLocationAsync(client, build, asset, assetLocations, releaseOutputDirectory, unifiedOutputDirectory, errors, downloadOutput);
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
            foreach (string error in errors)
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
        private async Task<DownloadedAsset> DownloadAssetFromAnyLocationAsync(HttpClient client,
                                                                            Build build, 
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
                var downloadedAsset = await DownloadAssetFromLocation(client,
                    build,
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
        private async Task<DownloadedAsset> DownloadAssetFromLatestLocation(HttpClient client,
                                                                            Build build,
                                                                            Asset asset,
                                                                            List<AssetLocation> assetLocations,
                                                                            string releaseOutputDirectory,
                                                                            string unifiedOutputDirectory,
                                                                            List<string> errors,
                                                                            StringBuilder downloadOutput)
        {
            AssetLocation latestLocation = assetLocations.OrderByDescending(al => al.Id).First();

            return await DownloadAssetFromLocation(client, 
                build, 
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
        private async Task<DownloadedAsset> DownloadAssetFromLocation(HttpClient client,
                                                                            Build build,
                                                                            Asset asset,
                                                                            AssetLocation location,
                                                                            string releaseOutputDirectory,
                                                                            string unifiedOutputDirectory,
                                                                            List<string> errors,
                                                                            StringBuilder downloadOutput)
        {
            var releaseSubPath = Path.Combine(releaseOutputDirectory, asset.NonShipping ? nonShippingSubPath : shippingSubPath);
            var unifiedSubPath = Path.Combine(unifiedOutputDirectory, asset.NonShipping ? nonShippingSubPath : shippingSubPath);

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
                    return await DownloadNugetPackageAsync(client, build, asset, location, releaseSubPath, unifiedSubPath, errors, downloadOutput);
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
        private async Task<DownloadedAsset> DownloadNugetPackageAsync(HttpClient client,
                                                                     Build build,
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

            string releaseFullSubPath = Path.Combine(releaseOutputDirectory, packagesSubPath);
            string unifiedFullSubPath = Path.Combine(unifiedOutputDirectory, packagesSubPath);
            string targetFileName = $"{asset.Name}.{asset.Version}.nupkg";
            // Construct the final path, using the correct casing rather than the blob feed casing.
            string releaseFullTargetPath = Path.Combine(releaseFullSubPath, targetFileName);
            string unifiedFullTargetPath = Path.Combine(unifiedFullSubPath, targetFileName);

            List<string> targetFilePaths = new List<string> { releaseFullTargetPath, unifiedFullTargetPath };

            DownloadedAsset downloadedAsset = new DownloadedAsset()
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
                string name = asset.Name.ToLowerInvariant();
                string version = asset.Version.ToLowerInvariant();
                string finalUri = assetLocation.Location.Substring(0, assetLocation.Location.Length - "index.json".Length);
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
            else if (IsAzureDevOpsFeedUrl(assetLocation.Location, out string feedAccount, out string feedProject, out string feedName))
            {
                string packageContentUri = await DownloadAssetFromAzureDevOpsFeedAsync(client, asset,
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
            string assetName = asset.Name;

            // Some blobs get pushed as packages. This is an artifact of a one-off issue in core-sdk
            // see https://github.com/dotnet/arcade/issues/4608 for an overall fix of this.
            // For now, if we get here, ensure that we ask for the package from the right location by
            // stripping off the leading path elements.
            if (!_options.NoWorkarounds)
            {
                assetName = Path.GetFileName(assetName);
            }

            string packageContentUrl = $"https://pkgs.dev.azure.com/{feedAccount}/{feedProject}_apis/packaging/feeds/{feedName}/nuget/packages/{assetName}/versions/{asset.Version}/content";

            if (string.IsNullOrEmpty(_options.AzureDevOpsPat))
            {
                LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);
                _options.AzureDevOpsPat = localSettings.AzureDevOpsToken;
            }

            AuthenticationHeaderValue authHeader = new AuthenticationHeaderValue(
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
            string normalizedAssetName = asset.Name;
            if (asset.Name.StartsWith("assets/"))
            {
                normalizedAssetName = asset.Name.Substring("assets/".Length);
            }

            string releaseFullSubPath = Path.Combine(releaseOutputDirectory, assetsSubPath);
            string unifiedFullSubPath = Path.Combine(unifiedOutputDirectory, assetsSubPath);
            // Construct the final path, using the correct casing rather than the blob feed casing.
            string releaseFullTargetPath = Path.Combine(releaseFullSubPath, normalizedAssetName);
            string unifiedFullTargetPath = Path.Combine(unifiedFullSubPath, normalizedAssetName);

            List<string> targetFilePaths = new List<string> { releaseFullTargetPath, unifiedFullTargetPath };

            DownloadedAsset downloadedAsset = new DownloadedAsset()
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
                string finalBaseUri = assetLocation.Location.Substring(0, assetLocation.Location.Length - "index.json".Length);
                string finalUri1 = $"{finalBaseUri}{asset.Name}";
                string finalUri2 = $"{finalBaseUri}assets/{asset.Name}";

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
                    string finalUri3 = $"{finalBaseUri}assets/assets/{asset.Name}";
                    using var cancellationTokenSource3 = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AssetDownloadTimeoutInSeconds));

                    if (await DownloadFileAsync(client, finalUri3, null, targetFilePaths, errors, downloadOutput, cancellationTokenSource3.Token))
                    {
                        downloadedAsset.Successful = true;
                        downloadedAsset.SourceLocation = finalUri3;
                        return downloadedAsset;
                    }

                    // Could also not be under /assets, so strip that from the url
                    string finalUri4 = finalUri1.Replace("assets/", "", StringComparison.OrdinalIgnoreCase);
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
            else if (IsAzureDevOpsFeedUrl(assetLocation.Location, out string feedAccount, out string feedProject, out string feedName))
            {
                // If we are here, it means this is a symbols package or a nupkg published as a blob,
                // remove some known paths from the name

                string replacedName = asset.Name.Replace("assets/", "", StringComparison.OrdinalIgnoreCase);
                replacedName = replacedName.Replace("symbols/", "", StringComparison.OrdinalIgnoreCase);
                replacedName = replacedName.Replace("sdk/", "", StringComparison.OrdinalIgnoreCase);
                replacedName = replacedName.Replace(".symbols", "", StringComparison.OrdinalIgnoreCase);
                replacedName = replacedName.Replace(".nupkg", "", StringComparison.OrdinalIgnoreCase);

                // blob packages sometimes confuse x86 and x64 as being part of the version
                string replacedVersion = asset.Version.Replace("64.", "");
                replacedVersion = replacedVersion.Replace("86.", "");

                // finally, remove the version from the name
                replacedName = replacedName.Replace($".{replacedVersion}", "", StringComparison.OrdinalIgnoreCase);

                // create a temp asset with the mangled name and version
                Asset mangledAsset = new Asset(asset.Id,
                    asset.BuildId,
                    asset.NonShipping,
                    replacedName,
                    replacedVersion,
                    asset.Locations);

                string packageContentUrl = await DownloadAssetFromAzureDevOpsFeedAsync(client,
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
                foreach (string targetFile in targetFilePaths)
                {
                    downloadOutput.AppendLine($"  {sourceUri} => {targetFile}.");
                }
                return true;
            }

            // Check all existing locations for the target file. If one exists, copy to the others.
            if (_options.SkipExisting)
            {
                string existingFile = targetFilePaths.FirstOrDefault(targetFile => File.Exists(targetFile));
                if (!string.IsNullOrEmpty(existingFile))
                {
                    foreach (string targetFile in targetFilePaths)
                    {
                        try
                        {
                            if (!File.Exists(targetFile))
                            {
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
            else
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
            string temporaryFileName = $"{targetFiles.First()}.inProgress";
            HttpRequestMessage requestMessage = null;

            try
            {
                await DeleteFileWithRetryAsync(temporaryFileName).ConfigureAwait(false);

                foreach (string targetFile in targetFiles)
                {
                    string directory = Path.GetDirectoryName(targetFile);
                    Directory.CreateDirectory(directory);
                }

                if (authHeader != null)
                {
                    requestMessage = new HttpRequestMessage(HttpMethod.Get, sourceUri)
                    {
                        Headers =
                        {
                            { HttpRequestHeader.Authorization.ToString(), authHeader.ToString() }
                        }
                    };
                }
                else
                {
                    requestMessage = new HttpRequestMessage(HttpMethod.Get, sourceUri);
                }

                // Ensure the parent target directory has been created.
                using (FileStream outStream = new FileStream(temporaryFileName,
                                                      _options.Overwrite ? FileMode.Create : FileMode.CreateNew,
                                                      FileAccess.Write))
                {
                    using (var response = await client.SendAsync(requestMessage))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var inStream = await response.Content.ReadAsStreamAsync())
                        {
                            downloadOutput.AppendLine($"    {sourceUri} =>");
                            foreach (string targetFile in targetFiles)
                            {
                                downloadOutput.AppendLine($"      {targetFile}");
                            }
                            await inStream.CopyToAsync(outStream, cancellationToken);
                        }
                    }
                }

                foreach (string targetFile in targetFiles)
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
                foreach (string targetFile in targetFiles)
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

                if (requestMessage != null)
                {
                    requestMessage.Dispose();
                }
            }
            return false;
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
}
