// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateDependenciesOperation : Operation
{
    private readonly UpdateDependenciesCommandLineOptions _options;
    private readonly ILogger<UpdateDependenciesOperation> _logger;
    private readonly IBarApiClient _barClient;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IRemoteTokenProvider _remoteTokenProvider;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;
    private readonly IFileSystem _fileSystem;

    public UpdateDependenciesOperation(
        UpdateDependenciesCommandLineOptions options,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IRemoteTokenProvider remoteTokenProvider,
        IGitRepoFactory gitRepoFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        ILogger<UpdateDependenciesOperation> logger,
        IFileSystem fileSystem)
    {
        _options = options;
        _logger = logger;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _remoteTokenProvider = remoteTokenProvider;
        _gitRepoFactory = gitRepoFactory;
        _coherencyUpdateResolver = coherencyUpdateResolver;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Update local dependencies based on a specific channel.
    /// </summary>
    /// <param name="options">Command line options</param>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Validate mutually exclusive options
            if (_options.CoherencyOnly && _options.NoCoherencyUpdates)
            {
                throw new ArgumentException("The --coherency-only and --no-coherency-updates options cannot be used together.");
            }

            // If subscription ID is provided, fetch subscription metadata and populate options
            if (!string.IsNullOrEmpty(_options.SubscriptionId))
            {
                await PopulateOptionsFromSubscriptionAsync();
            }

            var local = new Local(_remoteTokenProvider, _logger);
            var excludedAssetsMatcher = _options.ExcludedAssets?.Split(';').GetAssetMatcher()
                ?? new AssetMatcher(null);
            List<UnixPath> targetDirectories = ResolveTargetDirectories(local);

            ConcurrentDictionary<string, Task<ProductConstructionService.Client.Models.Build>> latestBuildTaskDictionary = new();
            foreach (var targetDirectory in targetDirectories)
            {
                await UpdateDependenciesInDirectory(targetDirectory, local, latestBuildTaskDictionary, excludedAssetsMatcher);
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            _logger.LogError(e.Message);
            return Constants.ErrorCode;
        }
        catch (Octokit.AuthorizationException)
        {
            _logger.LogError("Failed to update dependencies - GitHub token is invalid.");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update dependencies.");
            return Constants.ErrorCode;
        }
    }

    private List<UnixPath> ResolveTargetDirectories(Local local)
    {
        List<UnixPath> targetDirectories = [];
        if (string.IsNullOrEmpty(_options.TargetDirectory))
        {
            targetDirectories.Add(UnixPath.Empty);
        }
        else
        {
            targetDirectories = [];
            foreach (var dir in _options.TargetDirectory.Split(','))
            {
                if (dir.EndsWith('*'))
                {
                    var trimmedDir = dir.TrimEnd('/', '*');
                    var fullDirPath = new UnixPath(local.GetRepoRoot()) / trimmedDir;
                    targetDirectories.AddRange(_fileSystem.GetDirectories(fullDirPath).Select(p => new UnixPath(p.Substring(local.GetRepoRoot().Length + 1))));
                }
                else
                {
                    targetDirectories.Add(new UnixPath(dir));
                }
            }
        }

        return targetDirectories;
    }

    private async Task<int> NonCoherencyUpdatesForBuildAsync(
        ProductConstructionService.Client.Models.Build build,
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate,
        IAssetMatcher excludedAssetsMatcher,
        UnixPath relativeBasePath,
        Dictionary<string, string> assetRepoOrigins = null)
    {
        // Parse excluded repo origins if provided
        HashSet<string> excludedOrigins = null;
        if (!string.IsNullOrEmpty(_options.ExcludedRepoOrigins))
        {
            excludedOrigins = _options.ExcludedRepoOrigins
                .Split(';')
                .Select(o => o.Trim())
                .Where(o => !string.IsNullOrEmpty(o))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation($"Excluding assets from repo origins: {string.Join(", ", excludedOrigins)}");
        }

        List<AssetData> assetData = build.Assets
            .Where(a => !excludedAssetsMatcher.IsExcluded(a.Name, relativeBasePath))
            .Where(a =>
            {
                // If no repo origin filtering is requested, include the asset
                if (excludedOrigins == null || excludedOrigins.Count == 0)
                    return true;

                // If we don't have repo origin information, include the asset by default
                if (assetRepoOrigins == null || !assetRepoOrigins.TryGetValue(a.Name, out var origin))
                {
                    _logger.LogTrace($"Asset '{a.Name}' has no repo origin information, including by default.");
                    return true;
                }

                // Check if this asset's origin is in the excluded list
                if (excludedOrigins.Contains(origin))
                {
                    _logger.LogInformation($"Excluding asset '{a.Name}' from repo origin '{origin}'");
                    return false;
                }

                return true;
            })
            .Select(a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            })
            .ToList();

        // Now determine what needs to be updated.
        List<DependencyUpdate> updates = _coherencyUpdateResolver.GetRequiredNonCoherencyUpdates(
            build.GetRepository(),
            build.Commit,
            assetData,
            candidateDependenciesForUpdate);

        foreach (DependencyUpdate update in updates)
        {
            DependencyDetail from = update.From;
            DependencyDetail to = update.To;

            // Print out what we are going to do.	
            Console.WriteLine($"    Updating '{from.Name}': '{from.Version}' => '{to.Version}'"
                              + $" (from build '{build.AzureDevOpsBuildNumber}' of '{build.GetRepository()}')");

            // Replace in the current dependencies list so the correct data can be used in coherency updates.
            currentDependencies.Remove(from);
            currentDependencies.Add(to);

            // Final list of dependencies to update
            dependenciesToUpdate.Add(to);
        }

        return Constants.SuccessCode;
    }

    private async Task UpdateDependenciesInDirectory(
        UnixPath relativeBasePath,
        Local local,
        ConcurrentDictionary<string, Task<ProductConstructionService.Client.Models.Build>> latestBuildTaskDictionary,
        IAssetMatcher excludedAssetsMatcher)
    {
        List<DependencyDetail> dependenciesToUpdate = [];

        // Get a list of all dependencies, then a list of dependencies that the user asked to be updated.
        // The list of all dependencies will be updated as we go through the update algorithm with the "current set",
        // which is then fed to a coherency calculation later.
        List<DependencyDetail> currentDependencies = await local.GetDependenciesAsync(includePinned: false, relativeBasePath: relativeBasePath);

        // Figure out what to query for. Load Version.Details.xml and find all repository uris,
        // optionally restricted by the input dependency parameter.
        List<DependencyDetail> candidateDependenciesForUpdate = await local.GetDependenciesAsync(_options.Name, false, relativeBasePath);

        var dependenciesRelativeFolder = relativeBasePath == UnixPath.Empty
            ? "root"
            : relativeBasePath;
        Console.WriteLine($"Path {dependenciesRelativeFolder}");

        // If the source repository was specified, filter away any local dependencies not from that
        // source repository.
        if (!string.IsNullOrEmpty(_options.SourceRepository))
        {
            candidateDependenciesForUpdate = [.. candidateDependenciesForUpdate.Where(
                dependency => dependency.RepoUri.Contains(_options.SourceRepository, StringComparison.OrdinalIgnoreCase))];
        }

        if (candidateDependenciesForUpdate.Count == 0)
        {
            _logger.LogInformation("    Found no dependencies to update");
            return;
        }

        if (!string.IsNullOrEmpty(_options.Name) && !string.IsNullOrEmpty(_options.Version))
        {
            UpdateSpecificDependencyToSpecificVersion(candidateDependenciesForUpdate, dependenciesToUpdate);
        }
        else if (!string.IsNullOrEmpty(_options.PackagesFolder))
        {
            UpdateDependenciesFromLocalFolder(candidateDependenciesForUpdate, dependenciesToUpdate);
        }
        else if (!_options.CoherencyOnly)
        {
            if (string.IsNullOrEmpty(_options.Channel) && _options.BARBuildId == 0)
            {
                throw new ArgumentException("Please supply either a channel name (--channel), a packages folder (--packages-folder) " +
                                "a BAR build id (--id), or a specific dependency name and version (--name and --version).");
            }

            if (_options.BARBuildId > 0)
            {
                await RunNonCoherencyUpdateForSpecificBuild(
                    currentDependencies,
                    candidateDependenciesForUpdate,
                    dependenciesToUpdate,
                    excludedAssetsMatcher,
                    relativeBasePath);
            }
            else if (!string.IsNullOrEmpty(_options.Channel))
            {
                await RunNonCoherencyUpdateForChannel(
                    latestBuildTaskDictionary,
                    currentDependencies,
                    candidateDependenciesForUpdate,
                    dependenciesToUpdate,
                    excludedAssetsMatcher,
                    relativeBasePath);
            }
        }

        int coherencyResult = await CoherencyUpdatesAsync(currentDependencies, dependenciesToUpdate)
                        .ConfigureAwait(false);
        if (coherencyResult != Constants.SuccessCode)
        {
            throw new DarcException($"Failed to update coherent parent tied dependencies in {relativeBasePath}");
        }

        if (dependenciesToUpdate.Count == 0)
        {
            _logger.LogWarning("Found no dependencies to update");
            return;
        }

        if (!_options.DryRun)
        {
            Console.Write("    Applying updates...");
            await local.UpdateDependenciesAsync(dependenciesToUpdate, _remoteFactory, _gitRepoFactory, _barClient, relativeBasePath);
            Console.WriteLine("    done.");
        }
    }

    private async Task<int> CoherencyUpdatesAsync(
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> dependenciesToUpdate)
    {
        if (_options.NoCoherencyUpdates)
        {
            _logger.LogInformation("    Skipping coherency updates due to --no-coherency-updates option.");
            return Constants.SuccessCode;
        }

        Console.Write("    Checking for coherency updates...");

        List<DependencyUpdate> coherencyUpdates = null;
        try
        {
            // Now run a coherency update based on the current set of dependencies updated from the previous pass.
            coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(currentDependencies);
            Console.WriteLine("done.");
        }
        catch (DarcCoherencyException e)
        {
            Console.WriteLine("failed.");
            PrettyPrintCoherencyErrors(e);
            return Constants.ErrorCode;
        }

        foreach (DependencyUpdate dependencyUpdate in coherencyUpdates)
        {
            DependencyDetail from = dependencyUpdate.From;
            DependencyDetail to = dependencyUpdate.To;
            DependencyDetail coherencyParent = currentDependencies.First(d =>
                d.Name.Equals(from.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase));
            // Print out what we are going to do.	
            Console.WriteLine($"    Updating '{from.Name}': '{from.Version}' => '{to.Version}' " +
                              $"to ensure coherency with {from.CoherentParentDependencyName}@{coherencyParent.Version}");

            // Final list of dependencies to update
            dependenciesToUpdate.Add(to);
        }

        return Constants.SuccessCode;
    }

    private void PrettyPrintCoherencyErrors(DarcCoherencyException e)
    {
        var errorMessage = new StringBuilder("Coherency updates failed for the following dependencies:");
        foreach (var error in e.Errors)
        {
            errorMessage.AppendLine(
                $"  Unable to update {error.Dependency.Name} to have coherency with " +
                $"{error.Dependency.CoherentParentDependencyName}: {error.Error}");
            foreach (string potentialSolution in error.PotentialSolutions)
            {
                errorMessage.AppendLine($"    - {potentialSolution}");
            }
        }

        _logger.LogError(errorMessage.ToString());
    }

    private void UpdateSpecificDependencyToSpecificVersion(
        IReadOnlyList<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate)
    {
        DependencyDetail dependency = candidateDependenciesForUpdate[0];
        dependency.Version = _options.Version;
        dependenciesToUpdate.Add(dependency);

        Console.WriteLine($"    Updating '{dependency.Name}': '{dependency.Version}' => '{_options.Version}'");
    }

    private void UpdateDependenciesFromLocalFolder(
        IReadOnlyList<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate)
    {
        try
        {
            dependenciesToUpdate.AddRange(GetDependenciesFromPackagesFolder(_options.PackagesFolder, candidateDependenciesForUpdate));
        }
        catch (DarcException exc)
        {
            _logger.LogError(exc, "    Failed to update dependencies based on folder '{folder}'", _options.PackagesFolder);
            throw;
        }
    }

    private async Task RunNonCoherencyUpdateForSpecificBuild(
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate,
        IAssetMatcher excludedAssetsMatcher,
        UnixPath relativeBasePath)
    {
        try
        {
            var specificBuild = await _barClient.GetBuildAsync(_options.BARBuildId);

            // Download and parse MergedManifest.xml if repo origin filtering is requested
            Dictionary<string, string> assetRepoOrigins = null;
            if (!string.IsNullOrEmpty(_options.ExcludedRepoOrigins))
            {
                using var httpClient = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true })
                {
                    Timeout = TimeSpan.FromMinutes(2)
                };
                assetRepoOrigins = await ManifestHelper.GetAssetRepoOriginsAsync(specificBuild, httpClient, _logger);

                if (assetRepoOrigins == null)
                {
                    _logger.LogWarning($"Could not retrieve MergedManifest.xml from build {specificBuild.Id}. " +
                        "Repo origin filtering will not be applied.");
                }
            }

            int nonCoherencyResult = await NonCoherencyUpdatesForBuildAsync(
                specificBuild,
                currentDependencies,
                candidateDependenciesForUpdate,
                dependenciesToUpdate,
                excludedAssetsMatcher,
                relativeBasePath,
                assetRepoOrigins);
            if (nonCoherencyResult != Constants.SuccessCode)
            {
                _logger.LogError("    Failed to update non-coherent parent tied dependencies.");
                return;
            }

            string sourceRepo = specificBuild.GetRepository();
            string sourceBranch = specificBuild.GetBranch();

            _logger.LogInformation("    Local dependencies updated based on build with BAR id {barId} {azdoBuildNumber} from {sourceRepo}@{sourceBranch}",
                _options.BARBuildId,
                specificBuild.AzureDevOpsBuildNumber,
                sourceRepo,
                sourceBranch);
        }
        catch (RestApiException e) when (e.Response.Status == 404)
        {
            _logger.LogError("Could not find build with BAR id '{id}'.", _options.BARBuildId);
            throw;
        }
    }

    private async Task RunNonCoherencyUpdateForChannel(
        ConcurrentDictionary<string, Task<Build>> latestBuildTaskDictionary,
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate,
        IAssetMatcher excludedAssetsMatcher,
        UnixPath relativeBasePath)
    {
        // Start channel query.
        var channel = await _barClient.GetChannelAsync(_options.Channel)
            ?? throw new ArgumentException($"Could not find a channel named '{_options.Channel}'.");

        // Limit the number of BAR queries by grabbing the repo URIs and making a hash set.
        // We gather the latest build for any dependencies that aren't marked with coherent parent
        // dependencies, as those will be updated based on additional queries.
        HashSet<string> repositoryUrisForQuery = candidateDependenciesForUpdate
            .Where(dependency => string.IsNullOrEmpty(dependency.CoherentParentDependencyName))
            .Select(dependency => dependency.RepoUri)
            .ToHashSet();

        foreach (var repoToQuery in repositoryUrisForQuery)
        {
            if (latestBuildTaskDictionary.ContainsKey(repoToQuery))
            {
                continue;
            }
            Console.WriteLine($"    Looking up latest build of {repoToQuery} on {_options.Channel}");
            var latestBuild = _barClient.GetLatestBuildAsync(repoToQuery, channel.Id);
            latestBuildTaskDictionary.TryAdd(repoToQuery, latestBuild);
        }

        foreach (var repoToQuery in repositoryUrisForQuery)
        {
            var build = await latestBuildTaskDictionary[repoToQuery];
            if (build == null)
            {
                _logger.LogTrace("  No build of '{uri}' found on channel '{channel}'.", repoToQuery, _options.Channel);
                continue;
            }

            // Download and parse MergedManifest.xml if repo origin filtering is requested
            Dictionary<string, string> assetRepoOrigins = null;
            if (!string.IsNullOrEmpty(_options.ExcludedRepoOrigins))
            {
                using var httpClient = new HttpClient(new HttpClientHandler { CheckCertificateRevocationList = true })
                {
                    Timeout = TimeSpan.FromMinutes(2)
                };
                assetRepoOrigins = await ManifestHelper.GetAssetRepoOriginsAsync(build, httpClient, _logger);

                if (assetRepoOrigins == null)
                {
                    _logger.LogWarning($"Could not retrieve MergedManifest.xml from build {build.Id}. " +
                        "Repo origin filtering will not be applied.");
                }
            }

            int nonCoherencyResult = await NonCoherencyUpdatesForBuildAsync(
                build,
                currentDependencies,
                candidateDependenciesForUpdate,
                dependenciesToUpdate,
                excludedAssetsMatcher,
                relativeBasePath,
                assetRepoOrigins);
            if (nonCoherencyResult != Constants.SuccessCode)
            {
                throw new DarcException($"Failed to update non-coherent parent tied dependencies in {relativeBasePath}");
            }
        }
    }

    private static IEnumerable<DependencyDetail> GetDependenciesFromPackagesFolder(string pathToFolder, IEnumerable<DependencyDetail> dependencies)
    {
        Dictionary<string, string> dependencyVersionMap = [];

        // Not using Linq to make sure there are no duplicates
        foreach (DependencyDetail dependency in dependencies)
        {
            if (!dependencyVersionMap.ContainsKey(dependency.Name))
            {
                dependencyVersionMap.Add(dependency.Name, dependency.Version);
            }
        }

        List<DependencyDetail> updatedDependencies = [];

        if (!Directory.Exists(pathToFolder))
        {
            throw new DarcException($"Packages folder '{pathToFolder}' does not exist.");
        }

        IEnumerable<string> packages = Directory.GetFiles(pathToFolder, "*.nupkg");

        foreach (string package in packages)
        {
            ManifestMetadata manifestMetedata = PackagesHelper.GetManifestMetadata(package);

            if (!dependencyVersionMap.TryGetValue(manifestMetedata.Id, out var oldVersion))
            {
                continue;
            }

            Console.WriteLine($"Updating '{manifestMetedata.Id}': '{oldVersion}' => '{manifestMetedata.Version.OriginalVersion}'");

            updatedDependencies.Add(new DependencyDetail
            {
                Commit = manifestMetedata.Repository.Commit,
                Name = manifestMetedata.Id,
                RepoUri = manifestMetedata.Repository.Url,
                Version = manifestMetedata.Version.OriginalVersion,
            });
        }

        return updatedDependencies;
    }

    /// <summary>
    /// Fetch subscription metadata and populate command options based on subscription settings.
    /// This allows the subscription to be simulated using the existing update logic.
    /// </summary>
    private async Task PopulateOptionsFromSubscriptionAsync()
    {
        // Validate that subscription is not used with conflicting options
        if (!string.IsNullOrEmpty(_options.Channel))
        {
            throw new DarcException("The --subscription parameter cannot be used with --channel. The subscription already specifies a channel.");
        }

        if (!string.IsNullOrEmpty(_options.PackagesFolder))
        {
            throw new DarcException("The --subscription parameter cannot be used with --packages-folder.");
        }

        if (!string.IsNullOrEmpty(_options.Name) && !string.IsNullOrEmpty(_options.Version))
        {
            throw new DarcException("The --subscription parameter cannot be used with --name and --version. The subscription determines which dependencies to update.");
        }

        if (!string.IsNullOrEmpty(_options.SourceRepository))
        {
            throw new DarcException("The --subscription parameter cannot be used with --source-repo. The subscription already specifies a source repository.");
        }

        if (_options.CoherencyOnly)
        {
            throw new DarcException("The --subscription parameter cannot be used with --coherency-only.");
        }

        if (!string.IsNullOrEmpty(_options.TargetDirectory))
        {
            throw new DarcException("The --subscription parameter cannot be used with --target-directory. The subscription already specifies a target directory.");
        }

        // Parse and validate subscription ID
        if (!Guid.TryParse(_options.SubscriptionId, out Guid subscriptionId))
        {
            throw new DarcException($"Invalid subscription ID '{_options.SubscriptionId}'. Please provide a valid GUID.");
        }

        // Fetch subscription metadata
        Subscription subscription;
        try
        {
            subscription = await _barClient.GetSubscriptionAsync(subscriptionId)
                ?? throw new DarcException($"Subscription with ID '{subscriptionId}' not found.");
        }
        catch (RestApiException e) when (e.Response.Status == 404)
        {
            throw new DarcException($"Subscription with ID '{subscriptionId}' not found.", e);
        }

        // Check if subscription is source-enabled (VMR code flow)
        if (subscription.SourceEnabled)
        {
            throw new DarcException("Source-enabled subscriptions (VMR code flow) are not supported with --subscription. This parameter is only for dependency flow subscriptions.");
        }

        Console.WriteLine($"Simulating subscription '{subscription.Id}':");
        Console.WriteLine($"  Source: {subscription.SourceRepository} (channel: {subscription.Channel.Name})");
        Console.WriteLine($"  Target: {subscription.TargetRepository}#{subscription.TargetBranch}");

        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            Console.WriteLine($"  Target directory: {subscription.TargetDirectory}");
        }

        if (subscription.ExcludedAssets?.Count > 0)
        {
            Console.WriteLine($"  Excluded assets: {string.Join(", ", subscription.ExcludedAssets)}");
        }

        // Find the latest build from the source repository on the channel (unless build ID is provided)
        if (_options.BARBuildId == 0)
        {
            var latestBuild = await _barClient.GetLatestBuildAsync(subscription.SourceRepository, subscription.Channel.Id)
                ?? throw new DarcException($"No builds found for repository '{subscription.SourceRepository}' on channel '{subscription.Channel.Name}'.");

            Console.WriteLine($"  Latest build: {latestBuild.AzureDevOpsBuildNumber} (BAR ID: {latestBuild.Id})");
            Console.WriteLine($"  Build commit: {latestBuild.Commit}");
            _options.BARBuildId = latestBuild.Id;
        }
        else
        {
            Console.WriteLine($"  Using provided build ID: {_options.BARBuildId}");
        }

        Console.WriteLine();

        // Populate options from subscription settings
        _options.Channel = subscription.Channel.Name;
        _options.SourceRepository = subscription.SourceRepository;
        _options.TargetDirectory = subscription.TargetDirectory;

        // Use subscription's excluded assets only if not provided via command line
        if (string.IsNullOrEmpty(_options.ExcludedAssets) && subscription.ExcludedAssets?.Count > 0)
        {
            _options.ExcludedAssets = string.Join(";", subscription.ExcludedAssets);
        }
    }
}
