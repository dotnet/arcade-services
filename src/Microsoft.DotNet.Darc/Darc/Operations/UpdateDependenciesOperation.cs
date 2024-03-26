// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;

namespace Microsoft.DotNet.Darc.Operations;

internal class UpdateDependenciesOperation : Operation
{
    private readonly UpdateDependenciesCommandLineOptions _options;
    public UpdateDependenciesOperation(UpdateDependenciesCommandLineOptions options)
        : base(options)
    {
        _options = options;
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
            DarcSettings darcSettings = darcSettings = LocalSettings.GetDarcSettings(_options, Logger);

            // TODO: PAT only used for pulling the Arcade eng/common dir,
            // so hardcoded to GitHub PAT right now. Must be more generic in the future.
            darcSettings.GitType = GitRepoType.GitHub;
            LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);

            darcSettings.GitRepoPersonalAccessToken = localSettings != null && !string.IsNullOrEmpty(localSettings.GitHubToken) ?
                localSettings.GitHubToken :
                _options.GitHubPat;

            IRemoteFactory remoteFactory = Provider.GetRequiredService<IRemoteFactory>();
            IBarApiClient barClient = Provider.GetRequiredService<IBarApiClient>();
            var coherencyUpdateResolver = new CoherencyUpdateResolver(barClient, Logger);

            var local = new Local(_options.GetRemoteConfiguration(), Logger);
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
                Console.WriteLine("Found no dependencies to update.");
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
                    Logger.LogError(exc, $"Error: Failed to update dependencies based on folder '{_options.PackagesFolder}'");
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
                        var specificBuild = await barClient.GetBuildAsync(_options.BARBuildId);

                        int nonCoherencyResult = NonCoherencyUpdatesForBuild(specificBuild, coherencyUpdateResolver, currentDependencies, candidateDependenciesForUpdate, dependenciesToUpdate);
                        if (nonCoherencyResult != Constants.SuccessCode)
                        {
                            Console.WriteLine("Error: Failed to update non-coherent parent tied dependencies.");
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
                    Console.WriteLine($"Could not find build with BAR id '{_options.BARBuildId}'.");
                    return Constants.ErrorCode;
                }
            }
            else if (!_options.CoherencyOnly)
            {
                if (string.IsNullOrEmpty(_options.Channel))
                {
                    Console.WriteLine($"Please supply either a channel name (--channel), a packages folder (--packages-folder) " +
                                        "a BAR build id (--id), or a specific dependency name and version (--name and --version).");
                    return Constants.ErrorCode;
                }

                // Start channel query.
                Task<Channel> channel = barClient.GetChannelAsync(_options.Channel);

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
                    Console.WriteLine($"Could not find a channel named '{_options.Channel}'.");
                    return Constants.ErrorCode;
                }

                foreach (string repoToQuery in repositoryUrisForQuery)
                {
                    Console.WriteLine($"Looking up latest build of {repoToQuery} on {_options.Channel}");
                    var latestBuild = barClient.GetLatestBuildAsync(repoToQuery, channelInfo.Id);
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
                        Logger.LogTrace($"No build of '{repoUri}' found on channel '{_options.Channel}'.");
                        continue;
                    }

                    int nonCoherencyResult = NonCoherencyUpdatesForBuild(build, coherencyUpdateResolver, currentDependencies, candidateDependenciesForUpdate, dependenciesToUpdate);
                    if (nonCoherencyResult != Constants.SuccessCode)
                    {
                        Console.WriteLine("Error: Failed to update non-coherent parent tied dependencies.");
                        return nonCoherencyResult;
                    }
                }
            }

            int coherencyResult = await CoherencyUpdatesAsync(coherencyUpdateResolver, remoteFactory, currentDependencies, dependenciesToUpdate)
                        .ConfigureAwait(false);
            if (coherencyResult != Constants.SuccessCode)
            {
                Console.WriteLine("Error: Failed to update coherent parent tied dependencies.");
                return coherencyResult;
            }

            if (!dependenciesToUpdate.Any())
            {
                // If we found some dependencies already up to date,
                // then we consider this a success. Otherwise, we didn't even
                // find matching dependencies so we should let the user know.
                if (someUpToDate)
                {
                    Console.WriteLine($"All dependencies are up to date.");
                    return Constants.SuccessCode;
                }
                else
                {
                    Console.WriteLine($"Found no dependencies to update.");
                    return Constants.ErrorCode;
                }
            }

            if (_options.DryRun)
            {
                return Constants.SuccessCode;
            }

            // Now call the local updater to run the update
            var gitRepoFactory = ActivatorUtilities.CreateInstance<GitRepoFactory>(Provider, Path.GetTempPath());
            await local.UpdateDependenciesAsync(dependenciesToUpdate, remoteFactory, gitRepoFactory, barClient);

            Console.WriteLine(finalMessage);

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error: Failed to update dependencies.");
            return Constants.ErrorCode;
        }
    }

    private static int NonCoherencyUpdatesForBuild(
        Build build,
        ICoherencyUpdateResolver updateResolver,
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> candidateDependenciesForUpdate,
        List<DependencyDetail> dependenciesToUpdate)
    {
        IEnumerable<AssetData> assetData = build.Assets.Select(
            a => new AssetData(a.NonShipping)
            {
                Name = a.Name,
                Version = a.Version
            });

        // Now determine what needs to be updated.
        List<DependencyUpdate> updates = updateResolver.GetRequiredNonCoherencyUpdates(
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

    private static async Task<int> CoherencyUpdatesAsync(
        ICoherencyUpdateResolver updateResolver,
        IRemoteFactory remoteFactory,
        List<DependencyDetail> currentDependencies,
        List<DependencyDetail> dependenciesToUpdate)
    {
        Console.WriteLine("Checking for coherency updates...");

        List<DependencyUpdate> coherencyUpdates = null;
        try
        {
            // Now run a coherency update based on the current set of dependencies updated from the previous pass.
            coherencyUpdates = await updateResolver.GetRequiredCoherencyUpdatesAsync(currentDependencies, remoteFactory);
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

    private static void PrettyPrintCoherencyErrors(DarcCoherencyException e)
    {
        Console.WriteLine("Coherency updates failed for the following dependencies:");
        foreach (var error in e.Errors)
        {
            Console.WriteLine($"  Unable to update {error.Dependency.Name} to have coherency with " +
                              $"{error.Dependency.CoherentParentDependencyName}: {error.Error}");
            foreach (string potentialSolution in error.PotentialSolutions)
            {
                Console.WriteLine($"    - {potentialSolution}");
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
