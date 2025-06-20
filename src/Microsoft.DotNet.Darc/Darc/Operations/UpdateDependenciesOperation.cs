// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ICoherencyUpdateResolver _coherencyUpdateResolver;

    public UpdateDependenciesOperation(
        UpdateDependenciesCommandLineOptions options,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IGitRepoFactory gitRepoFactory,
        ICoherencyUpdateResolver coherencyUpdateResolver,
        ILogger<UpdateDependenciesOperation> logger)
    {
        _options = options;
        _logger = logger;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _gitRepoFactory = gitRepoFactory;
        _coherencyUpdateResolver = coherencyUpdateResolver;
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
            var local = new Local(_options.GetRemoteTokenProvider(), _logger);
            List<DependencyDetail> dependenciesToUpdate = [];
            bool someUpToDate = false;
            string finalMessage = $"Local dependencies updated from channel '{_options.Channel}'.";

            // Get a list of all dependencies, then a list of dependencies that the user asked to be updated.
            // The list of all dependencies will be updated as we go through the update algorithm with the "current set",
            // which is then fed to a coherency calculation later.
            List<DependencyDetail> currentDependencies = await local.GetDependenciesAsync(includePinned: false);

            // Figure out what to query for. Load Version.Details.xml and find all repository uris,
            // optionally restricted by the input dependency parameter.
            List<DependencyDetail> candidateDependenciesForUpdate = await local.GetDependenciesAsync(_options.Name, false);

            // If the source repository was specified, filter away any local dependencies not from that
            // source repository.
            if (!string.IsNullOrEmpty(_options.SourceRepository))
            {
                candidateDependenciesForUpdate = candidateDependenciesForUpdate.Where(
                    dependency => dependency.RepoUri.Contains(_options.SourceRepository, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!candidateDependenciesForUpdate.Any())
            {
                _logger.LogWarning("Found no dependencies to update.");
                return Constants.ErrorCode;
            }

            if (!string.IsNullOrEmpty(_options.Name) && !string.IsNullOrEmpty(_options.Version))
            {
                DependencyDetail dependency = candidateDependenciesForUpdate.First();
                dependency.Version = _options.Version;
                dependenciesToUpdate.Add(dependency);

                Console.WriteLine($"Updating '{dependency.Name}': '{dependency.Version}' => '{_options.Version}'");

                finalMessage = $"Local dependency {_options.Name} updated to version '{_options.Version}'.";
            }
            else if (!string.IsNullOrEmpty(_options.PackagesFolder))
            {
                try
                {
                    dependenciesToUpdate.AddRange(GetDependenciesFromPackagesFolder(_options.PackagesFolder, candidateDependenciesForUpdate));
                }
                catch (DarcException exc)
                {
                    _logger.LogError(exc, "Failed to update dependencies based on folder '{folder}'", _options.PackagesFolder);
                    return Constants.ErrorCode;
                }

                finalMessage = $"Local dependencies updated based on packages folder {_options.PackagesFolder}.";
            }
            else if (_options.BARBuildId > 0)
            {
                try
                {
                    if (!_options.CoherencyOnly)
                    {
                        Console.WriteLine($"Looking up build with BAR id {_options.BARBuildId}");
                        var specificBuild = await _barClient.GetBuildAsync(_options.BARBuildId);

                        int nonCoherencyResult = NonCoherencyUpdatesForBuild(specificBuild, currentDependencies, candidateDependenciesForUpdate, dependenciesToUpdate);
                        if (nonCoherencyResult != Constants.SuccessCode)
                        {
                            _logger.LogError("Failed to update non-coherent parent tied dependencies.");
                            return nonCoherencyResult;
                        }

                        string sourceRepo = specificBuild.GetRepository();
                        string sourceBranch = specificBuild.GetBranch();

                        finalMessage = $"Local dependencies updated based on build with BAR id {_options.BARBuildId} " +
                                       $"({specificBuild.AzureDevOpsBuildNumber} from {sourceRepo}@{sourceBranch})";
                    }

                    finalMessage = string.IsNullOrEmpty(finalMessage) ? "Local dependencies successfully updated." : finalMessage;
                }
                catch (RestApiException e) when (e.Response.Status == 404)
                {
                    _logger.LogError("Could not find build with BAR id '{id}'.", _options.BARBuildId);
                    return Constants.ErrorCode;
                }
            }
            else if (!_options.CoherencyOnly)
            {
                if (string.IsNullOrEmpty(_options.Channel))
                {
                    _logger.LogError("Please supply either a channel name (--channel), a packages folder (--packages-folder) " +
                                    "a BAR build id (--id), or a specific dependency name and version (--name and --version).");
                    return Constants.ErrorCode;
                }

                // Start channel query.
                Task<Channel> channel = _barClient.GetChannelAsync(_options.Channel);

                // Limit the number of BAR queries by grabbing the repo URIs and making a hash set.
                // We gather the latest build for any dependencies that aren't marked with coherent parent
                // dependencies, as those will be updated based on additional queries.
                HashSet<string> repositoryUrisForQuery = candidateDependenciesForUpdate
                    .Where(dependency => string.IsNullOrEmpty(dependency.CoherentParentDependencyName))
                    .Select(dependency => dependency.RepoUri)
                    .ToHashSet();

                var getLatestBuildTaskDictionary = new ConcurrentDictionary<string, Task<Build>>();

                Channel channelInfo = await channel;
                if (channelInfo == null)
                {
                    _logger.LogError("Could not find a channel named '{channel}'.", _options.Channel);
                    return Constants.ErrorCode;
                }

                foreach (string repoToQuery in repositoryUrisForQuery)
                {
                    Console.WriteLine($"Looking up latest build of {repoToQuery} on {_options.Channel}");
                    var latestBuild = _barClient.GetLatestBuildAsync(repoToQuery, channelInfo.Id);
                    getLatestBuildTaskDictionary.TryAdd(repoToQuery, latestBuild);
                }

                // For each build, first go through and determine the required updates,
                // updating the "live" dependency information as we go.
                // Then run a second pass where we update any assets based on coherency information.
                foreach (KeyValuePair<string, Task<Build>> buildKvPair in getLatestBuildTaskDictionary)
                {
                    string repoUri = buildKvPair.Key;
                    Build build = await buildKvPair.Value;

                    if (build == null)
                    {
                        _logger.LogTrace("No build of '{uri}' found on channel '{channel}'.",
                            repoUri,
                            _options.Channel);
                        continue;
                    }

                    int nonCoherencyResult = NonCoherencyUpdatesForBuild(build, currentDependencies, candidateDependenciesForUpdate, dependenciesToUpdate);
                    if (nonCoherencyResult != Constants.SuccessCode)
                    {
                        _logger.LogError("Failed to update non-coherent parent tied dependencies.");
                        return nonCoherencyResult;
                    }
                }
            }

            int coherencyResult = await CoherencyUpdatesAsync(currentDependencies, dependenciesToUpdate)
                        .ConfigureAwait(false);
            if (coherencyResult != Constants.SuccessCode)
            {
                _logger.LogError("Failed to update coherent parent tied dependencies.");
                return coherencyResult;
            }

            if (!dependenciesToUpdate.Any())
            {
                // If we found some dependencies already up to date,
                // then we consider this a success. Otherwise, we didn't even
                // find matching dependencies so we should let the user know.
                if (someUpToDate)
                {
                    Console.WriteLine("All dependencies are up to date.");
                    return Constants.SuccessCode;
                }
                else
                {
                    _logger.LogError("Found no dependencies to update.");
                    return Constants.ErrorCode;
                }
            }

            if (_options.DryRun)
            {
                return Constants.SuccessCode;
            }

            // Now call the local updater to run the update
            await local.UpdateDependenciesAsync(dependenciesToUpdate, _remoteFactory, _gitRepoFactory, _barClient);

            Console.WriteLine(finalMessage);

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

    private int NonCoherencyUpdatesForBuild(
        Build build,
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate)
    {
        List<AssetData> assetData = build.Assets
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
            Console.WriteLine($"Updating '{from.Name}': '{from.Version}' => '{to.Version}'"
                              + $" (from build '{build.AzureDevOpsBuildNumber}' of '{build.GetRepository()}')");

            // Replace in the current dependencies list so the correct data can be used in coherency updates.
            currentDependencies.Remove(from);
            currentDependencies.Add(to);

            // Final list of dependencies to update
            dependenciesToUpdate.Add(to);
        }

        return Constants.SuccessCode;
    }

    private async Task<int> CoherencyUpdatesAsync(
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> dependenciesToUpdate)
    {
        Console.WriteLine("Checking for coherency updates...");

        List<DependencyUpdate> coherencyUpdates = null;
        try
        {
            // Now run a coherency update based on the current set of dependencies updated from the previous pass.
            coherencyUpdates = await _coherencyUpdateResolver.GetRequiredCoherencyUpdatesAsync(currentDependencies);
        }
        catch (DarcCoherencyException e)
        {
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
            Console.WriteLine($"Updating '{from.Name}': '{from.Version}' => '{to.Version}' " +
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

            if (dependencyVersionMap.ContainsKey(manifestMetedata.Id))
            {
                string oldVersion = dependencyVersionMap[manifestMetedata.Id];

                Console.WriteLine($"Updating '{manifestMetedata.Id}': '{oldVersion}' => '{manifestMetedata.Version.OriginalVersion}'");

                updatedDependencies.Add(new DependencyDetail
                {
                    Commit = manifestMetedata.Repository.Commit,
                    Name = manifestMetedata.Id,
                    RepoUri = manifestMetedata.Repository.Url,
                    Version = manifestMetedata.Version.OriginalVersion,
                });
            }
        }

        return updatedDependencies;
    }
}
