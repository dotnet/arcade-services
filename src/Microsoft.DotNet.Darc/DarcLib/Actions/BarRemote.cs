// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public class BarRemote : IBarRemote
{
    private readonly IBarClient _barClient;
    private readonly ILogger _logger;

    public BarRemote(IBarClient barClient, ILogger logger)
    {
        _barClient = barClient;
        _logger = logger;
    }

    #region Subscription Operations

    public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
        string sourceRepo = null,
        string targetRepo = null,
        int? channelId = null)
        => await _barClient.GetSubscriptionsAsync(
            sourceRepo: sourceRepo,
            targetRepo: targetRepo,
            channelId: channelId);

    public async Task<Subscription> CreateSubscriptionAsync(
        string channelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        bool batchable,
        List<MergePolicy> mergePolicies,
        string failureNotificationTags)
        => await _barClient.CreateSubscriptionAsync(
            channelName,
            sourceRepo,
            targetRepo,
            targetBranch,
            updateFrequency,
            batchable,
            mergePolicies,
            failureNotificationTags);

    public async Task<Subscription> UpdateSubscriptionAsync(
        string subscriptionId,
        SubscriptionUpdate subscription)
        => await _barClient.UpdateSubscriptionAsync(GetSubscriptionGuid(subscriptionId), subscription);

    public async Task<Subscription> GetSubscriptionAsync(string subscriptionId)
        => await _barClient.GetSubscriptionAsync(GetSubscriptionGuid(subscriptionId));

    public async Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(
        string repoUri,
        string branch)
        => await _barClient.GetRepositoryMergePoliciesAsync(repoUri, branch);

    public async Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(
        string repoUri,
        string branch)
        => await _barClient.GetRepositoriesAsync(repoUri, branch);

    public async Task SetRepositoryMergePoliciesAsync(
        string repoUri,
        string branch,
        List<MergePolicy> mergePolicies)
        => await _barClient.SetRepositoryMergePoliciesAsync(repoUri, branch, mergePolicies ?? []);

    public async Task<Subscription> TriggerSubscriptionAsync(string subscriptionId)
        => await _barClient.TriggerSubscriptionAsync(GetSubscriptionGuid(subscriptionId));

    public async Task<Subscription> TriggerSubscriptionAsync(string subscriptionId, int sourceBuildId)
        => await _barClient.TriggerSubscriptionAsync(GetSubscriptionGuid(subscriptionId), sourceBuildId);

    public async Task<Subscription> DeleteSubscriptionAsync(string subscriptionId)
        => await _barClient.DeleteSubscriptionAsync(GetSubscriptionGuid(subscriptionId));

    #endregion

    #region Channel Operations

    public async Task<Channel> GetChannelAsync(string channel)
        => await _barClient.GetChannelAsync(channel);

    public async Task<Channel> GetChannelAsync(int channelId)
        => await _barClient.GetChannelAsync(channelId);

    public async Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(
        string repository = null,
        string branch = null,
        string channel = null)
        => await _barClient.GetDefaultChannelsAsync(repository: repository,
            branch: branch,
            channel: channel);

    public async Task AddDefaultChannelAsync(
        string repository,
        string branch,
        string channel)
        => await _barClient.AddDefaultChannelAsync(repository, branch, channel);

    public async Task DeleteDefaultChannelAsync(int id)
        => await _barClient.DeleteDefaultChannelAsync(id);

    public async Task UpdateDefaultChannelAsync(
        int id,
        string repository = null,
        string branch = null,
        string channel = null,
        bool? enabled = null)
        => await _barClient.UpdateDefaultChannelAsync(id, repository, branch, channel, enabled);

    public async Task<Channel> CreateChannelAsync(string name, string classification)
        => await _barClient.CreateChannelAsync(name, classification);

    public async Task<Channel> DeleteChannelAsync(int id)
        => await _barClient.DeleteChannelAsync(id);

    public async Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null)
        => await _barClient.GetChannelsAsync(classification);

    public async Task<DependencyFlowGraph> GetDependencyFlowGraphAsync(
        int channelId,
        int days,
        bool includeArcade,
        bool includeBuildTimes,
        bool includeDisabledSubscriptions,
        IReadOnlyList<string> includedFrequencies = default)
        => await _barClient.GetDependencyFlowGraphAsync(
            channelId,
            days,
            includeArcade,
            includeBuildTimes,
            includeDisabledSubscriptions,
            includedFrequencies);

    #endregion

    #region Build/Asset Operations

    public async Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
    {
        try
        {
            return await _barClient.GetLatestBuildAsync(repoUri: repoUri, channelId: channelId);
        }
        catch (RestApiException e) when (e.Response.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Build> GetBuildAsync(int buildId)
        => await _barClient.GetBuildAsync(buildId);

    public async Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
        => await _barClient.GetBuildsAsync(repoUri: repoUri, commit: commit);

    public async Task<IEnumerable<Asset>> GetAssetsAsync(
        string name = null,
        string version = null,
        int? buildId = null,
        bool? nonShipping = null)
        => await _barClient.GetAssetsAsync(
            name: name,
            version: version,
            buildId: buildId,
            nonShipping: nonShipping);

    public async Task AssignBuildToChannelAsync(int buildId, int channelId)
        => await _barClient.AssignBuildToChannelAsync(buildId, channelId);

    public async Task DeleteBuildFromChannelAsync(int buildId, int channelId)
        => await _barClient.DeleteBuildFromChannelAsync(buildId, channelId);

    public async Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate)
        => await _barClient.UpdateBuildAsync(buildId, buildUpdate);

    /// <summary>
    ///     Update a list of dependencies with asset locations.
    /// </summary>
    /// <param name="dependencies">Dependencies to load locations for</param>
    /// <returns>Async task</returns>
    public async Task AddAssetLocationToDependenciesAsync(IEnumerable<DependencyDetail> dependencies)
    {
        var buildCache = new Dictionary<int, Build>();

        foreach (var dependency in dependencies)
        {
            IEnumerable<Asset> matchingAssets = await GetAssetsAsync(dependency.Name, dependency.Version);
            List<Asset> matchingAssetsFromSameSha = [];

            foreach (var asset in matchingAssets)
            {
                if (!buildCache.TryGetValue(asset.BuildId, out Build producingBuild))
                {
                    producingBuild = await GetBuildAsync(asset.BuildId);
                    buildCache.Add(asset.BuildId, producingBuild);
                }

                if (producingBuild.Commit == dependency.Commit)
                {
                    matchingAssetsFromSameSha.Add(asset);
                }
            }

            // Always look at the 'latest' asset to get the right asset even in stable build scenarios
            var latestAsset = matchingAssetsFromSameSha.OrderByDescending(a => a.BuildId).FirstOrDefault();
            if (latestAsset != null)
            {
                IEnumerable<string> currentAssetLocations = latestAsset.Locations?
                    .Where(l => l.Type == LocationType.NugetFeed)
                    .Select(l => l.Location);

                if (currentAssetLocations == null)
                {
                    continue;
                }

                dependency.Locations = currentAssetLocations;
            }
        }
    }

    #endregion

    #region Goal Operations
    public async Task<Goal> SetGoalAsync(string channel, int definitionId, int minutes)
        => await _barClient.SetGoalAsync(channel, definitionId, minutes);

    public async Task<Goal> GetGoalAsync(string channel, int definitionId)
        => await _barClient.GetGoalAsync(channel, definitionId);

    public async Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days)
        => await _barClient.GetBuildTimeAsync(defaultChannelId, days);

    #endregion

    /// <summary>
    /// Convert subscription id to a Guid, throwing if not valid.
    /// </summary>
    /// <param name="subscriptionId">ID of subscription.</param>
    /// <returns>New guid</returns>
    private static Guid GetSubscriptionGuid(string subscriptionId)
    {
        if (!Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
        {
            throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
        }
        return subscriptionGuid;
    }

    #region Repo/Dependency Operations

    public async Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
        IEnumerable<DependencyDetail> dependencies,
        IRemoteFactory remoteFactory)
        => await GetRequiredStrictCoherencyUpdatesAsync(dependencies, remoteFactory);

    /// <summary>
    ///     Determine what dependencies need to be updated given an input set of assets.
    /// </summary>
    /// <param name="sourceCommit">Commit the assets come from.</param>
    /// <param name="assets">Assets as inputs for the update.</param>
    /// <param name="dependencies">Current set of the dependencies.</param>
    /// <param name="sourceRepoUri">Repository the assets came from.</param>
    /// <returns>Map of existing dependency->updated dependency</returns>
    public Task<List<DependencyUpdate>> GetRequiredNonCoherencyUpdatesAsync(
        string sourceRepoUri,
        string sourceCommit,
        IEnumerable<AssetData> assets,
        IEnumerable<DependencyDetail> dependencies)
    {
        Dictionary<DependencyDetail, DependencyDetail> toUpdate = [];

        // Walk the assets, finding the dependencies that don't have coherency markers.
        // those must be updated in a second pass.
        foreach (AssetData asset in assets)
        {
            DependencyDetail matchingDependencyByName =
                dependencies.FirstOrDefault(d => d.Name.Equals(asset.Name, StringComparison.OrdinalIgnoreCase) &&
                                                 string.IsNullOrEmpty(d.CoherentParentDependencyName));

            if (matchingDependencyByName == null)
            {
                continue;
            }

            // If the dependency is pinned, don't touch it.
            if (matchingDependencyByName.Pinned)
            {
                continue;
            }

            // Build might contain multiple assets of the same name
            if (toUpdate.ContainsKey(matchingDependencyByName))
            {
                continue;
            }

            // Check if an update is actually needed.
            // Case-sensitive compare as case-correction is desired.
            if (matchingDependencyByName.Name == asset.Name &&
                matchingDependencyByName.Version == asset.Version &&
                matchingDependencyByName.Commit == sourceCommit &&
                matchingDependencyByName.RepoUri == sourceRepoUri)
            {
                continue;
            }

            var newDependency = new DependencyDetail(matchingDependencyByName)
            {
                Commit = sourceCommit,
                RepoUri = sourceRepoUri,
                Version = asset.Version,
                Name = asset.Name,
                Locations = asset.Locations?.Select(l => l.Location)
            };

            toUpdate.Add(matchingDependencyByName, newDependency);
        }

        List<DependencyUpdate> dependencyUpdates = toUpdate
            .Select(kv => new DependencyUpdate
            {
                From = kv.Key,
                To = kv.Value
            })
            .ToList();

        return Task.FromResult(dependencyUpdates);
    }

    /// <summary>
    ///     Get updates required by coherency constraints using the "strict" algorithm
    /// </summary>
    /// <param name="dependencies">Current set of dependencies.</param>
    /// <param name="remoteFactory">Remote factory for remote queries.</param>
    /// <returns>Dependencies with updates.</returns>
    /// <remarks>
    ///     'Strict' coherency is a version of coherency that does not **require** any build information,
    ///     though it can be used when attempting to disambiguate multiple builds of the same commit.
    ///     The traditional 'legacy' coherency algorithm works by identifying the version of an asset
    ///     coming from the **newest build** below the identified CPD parent. This means a few things:
    ///     - You need to build a dependency graph
    ///     - You need to traverse all nodes in the graph
    ///     - If there is incremental servicing in some places, it's possible for dependencies to get
    ///       unintentionally downgraded (see https://github.com/dotnet/arcade/issues/5195).
    ///
    ///     Fundamentally, strict coherency does the same thing as regular coherency, but with a limited
    ///     search space and no build information required. In the case where dependency A has CPD B....
    ///     - The version search is only one level deep.
    ///     - B's repo+sha must contain dependency A in its version.details.xml file.
    ///
    ///     Because B's repo+sha may only have one version of A, this eliminates the need for any kind of version
    ///     check and vastly simplifies the algorithm. The downside is that more repos must bubble up dependencies,
    ///     but this is fairly minimal and generally covered by the need to have dependencies explicit in the
    ///     version details files anyway.
    /// </remarks>
    private async Task<List<DependencyUpdate>> GetRequiredStrictCoherencyUpdatesAsync(
        IEnumerable<DependencyDetail> dependencies,
        IRemoteFactory remoteFactory)
    {
        List<DependencyUpdate> toUpdate = [];
        IEnumerable<DependencyDetail> leavesOfCoherencyTrees = CalculateLeavesOfCoherencyTrees(dependencies);

        if (!leavesOfCoherencyTrees.Any())
        {
            // Nothing to do.
            return toUpdate;
        }

        Dictionary<string, CoherencyError> coherencyErrors = [];

        // Cache of dependencies. Key is "<repo>@<sha>".
        Dictionary<string, IEnumerable<DependencyDetail>> dependenciesCache = [];
        // Cache of builds with assets. Key is "<repo>@<sha>".
        Dictionary<string, List<Build>> buildCache = [];
        // Cache of nuget config files for further build disambiguation. Key is "<repo>@<sha>".
        Dictionary<string, IEnumerable<string>> nugetConfigCache = [];

        // Now make a walk over coherent dependencies. Note that coherent dependencies could make
        // a chain (A->B->C). In all cases we need to walk to the head of the chain, keeping track
        // of all elements in the chain, then updating then in reverse order (C, B, A).
        foreach (DependencyDetail dependency in leavesOfCoherencyTrees)
        {
            // Build the update stack.
            // Walk to head of dependency tree, keeping track of elements along the way.
            // If we hit a pinned dependency in the walk, that means we can't move
            // the dependency and therefore it is effectively the "head" of the subtree.
            // We will still visit all the elements in the chain eventually in this algorithm:
            // Consider A->B(pinned)->C(pinned)->D.
            var updateStack = new Stack<DependencyDetail>();
            DependencyDetail currentDependency = dependency;
            while (!string.IsNullOrEmpty(currentDependency.CoherentParentDependencyName) && !currentDependency.Pinned)
            {
                updateStack.Push(currentDependency);
                DependencyDetail parentCoherentDependency = dependencies.FirstOrDefault(d =>
                    d.Name.Equals(currentDependency.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase));
                currentDependency = parentCoherentDependency ?? throw new DarcException($"Dependency {currentDependency.Name} has non-existent parent " +
                    $"dependency {currentDependency.CoherentParentDependencyName}");
            }

            while (updateStack.Count > 0)
            {
                DependencyDetail dependencyToUpdate = updateStack.Pop();

                // Get the coherent parent info. Note that the coherent parent could have
                // been updated, so we look in the toUpdate list first to find the updated info
                DependencyDetail parentCoherentDependency = toUpdate
                    .FirstOrDefault(d => d.To.Name.Equals(dependencyToUpdate.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase))?
                    .To;

                // Not current in the update list, so look up in the original dependencies.
                if (parentCoherentDependency == null)
                {
                    parentCoherentDependency = dependencies.FirstOrDefault(d =>
                        d.Name.Equals(dependencyToUpdate.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase));
                }

                if (parentCoherentDependency == null)
                {
                    throw new DarcException("Unexpected error finding coherent parent dependency " +
                                            "in either original or updated dependencies list");
                }

                var parentCoherentDependencyCacheKey = $"{parentCoherentDependency.RepoUri}@{parentCoherentDependency.Commit}";

                // Get the dependencies at currentDependency's repo+sha.
                if (!dependenciesCache.TryGetValue(parentCoherentDependencyCacheKey,
                        out IEnumerable<DependencyDetail> coherentParentsDependencies))
                {
                    IRemote remoteClient = await remoteFactory.GetRemoteAsync(parentCoherentDependency.RepoUri, _logger);
                    coherentParentsDependencies = await remoteClient.GetDependenciesAsync(
                        parentCoherentDependency.RepoUri,
                        parentCoherentDependency.Commit);
                    dependenciesCache.Add(parentCoherentDependencyCacheKey, coherentParentsDependencies);
                }

                // Look up the dependency in the CPD
                var cpdDependency = coherentParentsDependencies.FirstOrDefault(dep => dep.Name.Equals(dependencyToUpdate.Name, StringComparison.OrdinalIgnoreCase));

                if (cpdDependency == null)
                {
                    // This is an invalid state. The dependency should be listed in the cpd parent's version details file.
                    var coherencyErrorKey = $"{parentCoherentDependency.RepoUri}{parentCoherentDependency.Commit}{dependencyToUpdate.Name}";
                    if (!coherencyErrors.ContainsKey(coherencyErrorKey))
                    {
                        coherencyErrors.Add(coherencyErrorKey, new CoherencyError()
                        {
                            Dependency = dependencyToUpdate,
                            Error = $"{parentCoherentDependency.RepoUri} @ {parentCoherentDependency.Commit} does not contain dependency {dependencyToUpdate.Name}",
                            PotentialSolutions = new List<string> {
                                $"Add the dependency to {parentCoherentDependency.RepoUri}.",
                                $"Pin the dependency.",
                                "Remove the CoherentParentDependency attribute."
                            }
                        });
                    }

                    // This invalidates any remaining chain we were attempting to update, since any updates
                    // up the chain would change results down the chain.
                    updateStack.Clear();
                    continue;
                }

                // Check whether it is already up to date.
                if (dependencyToUpdate.Name.Equals(cpdDependency.Name) &&
                    dependencyToUpdate.Version.Equals(cpdDependency.Version) &&
                    dependencyToUpdate.Commit.Equals(cpdDependency.Commit) &&
                    dependencyToUpdate.RepoUri.Equals(cpdDependency.RepoUri))
                {
                    continue;
                }

                _logger.LogInformation($"Dependency {dependencyToUpdate.Name} will be updated to " +
                                       $"{cpdDependency.Version} from {cpdDependency.RepoUri}@{cpdDependency.Commit}.");

                Asset coherentAsset = await DisambiguateAssetsAsync(remoteFactory, buildCache, nugetConfigCache,
                    parentCoherentDependency, cpdDependency);

                var updatedDependency = new DependencyDetail(dependencyToUpdate)
                {
                    Name = cpdDependency.Name,
                    Version = cpdDependency.Version,
                    RepoUri = cpdDependency.RepoUri,
                    Commit = cpdDependency.Commit,
                    Locations = coherentAsset?.Locations?.Select(l => l.Location)
                };

                toUpdate.Add(new DependencyUpdate
                {
                    From = dependencyToUpdate,
                    To = updatedDependency
                });
            }
        }

        if (coherencyErrors.Any())
        {
            throw new DarcCoherencyException(coherencyErrors.Values);
        }

        return toUpdate;
    }

    /// <summary>
    ///     Calculate the leaves of the coherency trees
    /// </summary>
    /// <param name="dependencies">Dependencies to find leaves for.</param>
    /// <remarks>
    ///     Leaves of the coherent dependency trees.  Basically
    ///     this means that the coherent dependency is not
    ///     pointed to by another dependency, or is pointed to by only
    ///     pinned dependencies.
    ///
    ///     Examples:
    ///         - A->B(pinned)->C->D(pinned)
    ///         - C
    ///         - A->B->C->D
    ///         - D
    ///         - A->B
    ///         - B
    ///         - A->B->C(pinned)->D
    ///         - D
    ///         - B
    ///         - A->B(pinned)->C(pinned)
    ///         - None
    /// </remarks>
    /// <returns>Leaves of coherency trees</returns>
    private IEnumerable<DependencyDetail> CalculateLeavesOfCoherencyTrees(IEnumerable<DependencyDetail> dependencies)
    {
        // First find dependencies with coherent parent pointers.
        IEnumerable<DependencyDetail> leavesOfCoherencyTrees =
            dependencies.Where(d => !string.IsNullOrEmpty(d.CoherentParentDependencyName));

        // Then walk all of these and find all of those that are not pointed to by
        // other dependencies that are not pinned.
        // See above example for information on what this looks like.
        leavesOfCoherencyTrees = leavesOfCoherencyTrees.Where(potentialLeaf =>
        {
            bool pointedToByNonPinnedDependencies = dependencies.Any(otherLeaf =>
            {
                return !string.IsNullOrEmpty(otherLeaf.CoherentParentDependencyName) &&
                       otherLeaf.CoherentParentDependencyName.Equals(potentialLeaf.Name, StringComparison.OrdinalIgnoreCase) &&
                       !otherLeaf.Pinned;
            });
            return !pointedToByNonPinnedDependencies;
        });

        return leavesOfCoherencyTrees;
    }

    /// <summary>
    /// Disambiguate a set of potential assets based the nuget config
    /// file in a repo. The asset's locations are returned if a match is found.
    /// </summary>
    /// <param name="remoteFactory">Remote factory for looking up the nuget config.</param>
    /// <param name="buildCache">Cache of builds</param>
    /// <param name="nugetConfigCache">Cache of nuget config files</param>
    /// <param name="parentCoherentDependency">Parent dependency of <paramref name="cpdDependency"/></param>
    /// <param name="cpdDependency">Dependency to disambiguate on.</param>
    /// <returns>Asset if a match to nuget.config is found. Asset from newest build is returned </returns>
    private async Task<Asset> DisambiguateAssetsAsync(IRemoteFactory remoteFactory,
        Dictionary<string, List<Build>> buildCache, Dictionary<string, IEnumerable<string>> nugetConfigCache,
        DependencyDetail parentCoherentDependency, DependencyDetail cpdDependency)
    {
        var parentCoherentDependencyCacheKey = $"{parentCoherentDependency.RepoUri}@{parentCoherentDependency.Commit}";

        _logger.LogInformation($"Attempting to disambiguate {cpdDependency.Name}@{cpdDependency.Version} " +
                               $"based on nuget.config at {parentCoherentDependencyCacheKey}");

        var assetComparer = new AssetComparer();

        // Because stable assets can have specialized feeds which need
        // to be added to the nuget.config so that the assets can be accessed,
        // we need to look up the asset information for this
        if (!buildCache.TryGetValue($"{cpdDependency.RepoUri}@{cpdDependency.Commit}", out List<Build> potentialBuilds))
        {
            potentialBuilds = (await GetBuildsAsync(cpdDependency.RepoUri, cpdDependency.Commit)).ToList();
        }

        // Builds are ordered newest to oldest in the cache. Most of the time there
        // will be no more than one build here, but occasionally there are additional builds
        // generated for the same commit. They could even differ based on repo uri. Consider the
        // following scenarios:
        // - Two branches are pushed to internal with the same commit and both run builds. Only one publishes
        // - A sha is built internally, then pushed to github and built again.
        // - Same branch is built twice.
        // So, identifying the locations is not easy. Let's walk through the decision tree:

        // Case 1 - Only one build, which contains the asset. Just pick that asset's locations.
        Asset coherentAsset;
        if (potentialBuilds.Count == 1)
        {
            coherentAsset = potentialBuilds.Single().Assets.FirstOrDefault(
                asset => assetComparer.Equals(asset, cpdDependency));
        }
        // Cases where there are multiple builds. This is where it gets interesting.
        // We really want the same asset that the CPD parent has. The nuget.config
        // file there is a good way to disambiguate. The only real interesting case here
        // is if an asset location matches up with a single isolated feed in the CPD's nuget.config
        // In that case, we know that it is that specific asset this CPD is referencing. Any
        // time multiple feeds match and/or if the feeds are 'generic' (like nuget.org or dotnet5),
        // the choice becomes arbitrary as maestro doesn't manage the nuget.config for those feeds anyway.
        // So all we really need to do is get the location information for the assets, match it up
        // with the input nuget feeds. Any asset that matches goes in the list. The only interesting case is
        // where only one asset matches, in which case disambiguation succeeds and we we update based on that
        // asset, which may involve a change to nuget.config. In cases where 0 or multiple match, just return
        // the newest build.
        else if (potentialBuilds.Count > 1)
        {
            // Gather all matching assets from each of the builds.
            List<Build> buildsWithMatchingAssets = potentialBuilds
                .Where(build => build.Assets.Any(asset => assetComparer.Equals(asset, cpdDependency)))
                .OrderByDescending(build => build.Id)
                .ToList();

            List<Asset> allMatchingAssets = buildsWithMatchingAssets
                .Select(build => build.Assets.FirstOrDefault(asset => assetComparer.Equals(asset, cpdDependency)))
                .ToList();

            // If there is one or zero matching assets, just return what we have.
            if (allMatchingAssets.Count <= 1)
            {
                return allMatchingAssets.FirstOrDefault();
            }

            // Note that we use the parentCoherentDependencyCacheKey here because we want to know what feeds
            // the cpd's repo+sha used, and then match that with the location information for the
            // coherent asset itself.
            if (!nugetConfigCache.TryGetValue(parentCoherentDependencyCacheKey, out IEnumerable<string> nugetFeeds))
            {
                IRemote remoteClient = await remoteFactory.GetRemoteAsync(parentCoherentDependency.RepoUri, _logger);
                nugetFeeds = await remoteClient.GetPackageSourcesAsync(parentCoherentDependency.RepoUri, parentCoherentDependency.Commit);
            }

            // Find assets with locations that match any feed in the nuget.config file.
            var assetsWithMatchingLocations = allMatchingAssets
                .Where(asset =>
                {
                    if (asset.Locations == null)
                    {
                        return false;
                    }

                    return asset.Locations
                            .Select(location => location.Location)
                            .Intersect(nugetFeeds)
                            .Any();
                })
                .ToList();

            if (assetsWithMatchingLocations.Count != 1)
            {
                // Find the newest build in the matching assets
                return buildsWithMatchingAssets
                    .First()
                    .Assets
                    .FirstOrDefault(asset => assetComparer.Equals(asset, cpdDependency));
            }
            else
            {
                coherentAsset = assetsWithMatchingLocations.First();
            }
        }
        // Fallback - No builds. Do nothing
        else
        {
            coherentAsset = null;
        }

        return coherentAsset;
    }

    #endregion
}
