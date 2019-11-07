// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    class UpdateDependenciesOperation : Operation
    {
        UpdateDependenciesCommandLineOptions _options;
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

                // TODO: PAT only used for pulling the arcade eng/common dir,
                // so hardcoded to GitHub PAT right now. Must be more generic in the future.
                darcSettings.GitType = GitRepoType.GitHub;
                LocalSettings localSettings = LocalSettings.LoadSettingsFile(_options);

                darcSettings.GitRepoPersonalAccessToken = localSettings != null && !string.IsNullOrEmpty(localSettings.GitHubToken) ?
                                                    localSettings.GitHubToken :
                                                    _options.GitHubPat;

                IRemoteFactory remoteFactory = new RemoteFactory(_options);
                IRemote barOnlyRemote = await remoteFactory.GetBarOnlyRemoteAsync(Logger);
                Local local = new Local(Logger);
                List<DependencyDetail> dependenciesToUpdate = new List<DependencyDetail>();
                bool someUpToDate = false;
                string finalMessage = $"Local dependencies updated from channel '{_options.Channel}'.";

                // First we need to figure out what to query for.  Load Version.Details.xml and
                // find all repository uris, optionally restricted by the input dependency parameter.
                IEnumerable<DependencyDetail> localDependencies = await local.GetDependenciesAsync(_options.Name, false);

                // If the source repository was specified, filter away any local dependencies not from that
                // source repository.
                if (!string.IsNullOrEmpty(_options.SourceRepository))
                {
                    localDependencies = localDependencies.Where(
                        dependency => dependency.RepoUri.Contains(_options.SourceRepository, StringComparison.OrdinalIgnoreCase));
                }

                if (!localDependencies.Any())
                {
                    Console.WriteLine("Found no dependencies to update.");
                    return Constants.ErrorCode;
                }

                List<DependencyDetail> currentDependencies = localDependencies.ToList();

                if (!string.IsNullOrEmpty(_options.Name) && !string.IsNullOrEmpty(_options.Version))
                {
                    DependencyDetail dependency = currentDependencies.First();
                    dependency.Version = _options.Version;
                    dependenciesToUpdate.Add(dependency);

                    Console.WriteLine($"Updating '{dependency.Name}': '{dependency.Version}' => '{_options.Version}'");

                    finalMessage = $"Local dependency {_options.Name} updated to version '{_options.Version}'.";
                }
                else if (!string.IsNullOrEmpty(_options.PackagesFolder))
                {
                    try
                    {
                        dependenciesToUpdate.AddRange(GetDependenciesFromPackagesFolder(_options.PackagesFolder, currentDependencies));
                    }
                    catch (DarcException exc)
                    {
                        Logger.LogError(exc, $"Error: Failed to update dependencies based on folder '{_options.PackagesFolder}'");
                        return Constants.ErrorCode;
                    }

                    finalMessage = $"Local dependencies updated based on packages folder {_options.PackagesFolder}.";
                }
                else
                {
                    if (!_options.CoherencyOnly)
                    {
                        if (string.IsNullOrEmpty(_options.Channel))
                        {
                            Console.WriteLine($"Please supply either a channel name (--channel), a packages folder (--packages-folder) " +
                                $"or a specific dependency name and version (--name and --version).");
                            return Constants.ErrorCode;
                        }

                        // Start channel query.
                        Task<Channel> channel = barOnlyRemote.GetChannelAsync(_options.Channel);

                        // Limit the number of BAR queries by grabbing the repo URIs and making a hash set.
                        // We gather the latest build for any dependencies that aren't marked with coherent parent
                        // dependencies, as those will be updated based on additional queries.
                        HashSet<string> repositoryUrisForQuery = currentDependencies
                            .Where(dependency => string.IsNullOrEmpty(dependency.CoherentParentDependencyName))
                            .Select(dependency => dependency.RepoUri)
                            .ToHashSet();

                        ConcurrentDictionary<string, Task<Build>> getLatestBuildTaskDictionary = new ConcurrentDictionary<string, Task<Build>>();

                        Channel channelInfo = await channel;
                        if (channelInfo == null)
                        {
                            Console.WriteLine($"Could not find a channel named '{_options.Channel}'.");
                            return Constants.ErrorCode;
                        }

                        foreach (string repoToQuery in repositoryUrisForQuery)
                        {
                            Console.WriteLine($"Looking up latest build of {repoToQuery} on {_options.Channel}");
                            var latestBuild = barOnlyRemote.GetLatestBuildAsync(repoToQuery, channelInfo.Id);
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
                            IEnumerable<AssetData> assetData = build.Assets.Select(
                                a => new AssetData(a.NonShipping)
                                {
                                    Name = a.Name,
                                    Version = a.Version
                                });

                            // Now determine what needs to be updated.
                            List<DependencyUpdate> updates = await barOnlyRemote.GetRequiredNonCoherencyUpdatesAsync(
                                repoUri, build.Commit, assetData, currentDependencies);

                            foreach (DependencyUpdate update in updates)
                            {
                                DependencyDetail from = update.From;
                                DependencyDetail to = update.To;
                                // Print out what we are going to do.	
                                Console.WriteLine($"Updating '{from.Name}': '{from.Version}' => '{to.Version}'" +
                                    $" (from build '{build.AzureDevOpsBuildNumber}' of '{repoUri}')");

                                // Final list of dependencies to update
                                dependenciesToUpdate.Add(to);
                                // Replace in the current dependencies list so the correct data is fed into the coherency pass.
                                currentDependencies.Remove(from);
                                currentDependencies.Add(to);
                            }
                        }
                    }

                    Console.WriteLine("Checking for coherency updates...");

                    // Now run a coherency update based on the current set of dependencies updated
                    // from the previous pass.
                    List<DependencyUpdate> coherencyUpdates =
                        await barOnlyRemote.GetRequiredCoherencyUpdatesAsync(currentDependencies, remoteFactory);

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

                // Now call the local updater to run the update.
                await local.UpdateDependenciesAsync(dependenciesToUpdate, remoteFactory);

                Console.WriteLine(finalMessage);

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to update dependencies to channel {_options.Channel}");
                return Constants.ErrorCode;
            }
        }

        private IEnumerable<DependencyDetail> GetDependenciesFromPackagesFolder(string pathToFolder, IEnumerable<DependencyDetail> dependencies)
        {
            Dictionary<string, string> dependencyVersionMap = new Dictionary<string, string>();

            // Not using Linq to make sure there are no duplicates
            foreach (DependencyDetail dependency in dependencies)
            {
                if (!dependencyVersionMap.ContainsKey(dependency.Name))
                {
                    dependencyVersionMap.Add(dependency.Name, dependency.Version);
                }
            }

            List<DependencyDetail> updatedDependencies = new List<DependencyDetail>();

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
}
