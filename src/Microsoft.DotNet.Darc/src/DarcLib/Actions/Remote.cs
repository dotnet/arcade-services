// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib
{
    public class Remote : IRemote
    {
        private readonly IBarClient _barClient;
        private readonly GitFileManager _fileManager;
        private readonly IGitRepo _gitClient;
        private readonly ILogger _logger;

        public Remote(IGitRepo gitClient, IBarClient barClient, ILogger logger)
        {
            _logger = logger;
            _barClient = barClient;
            _gitClient = gitClient;

            if (_gitClient != null)
            {
                _fileManager = new GitFileManager(_gitClient, _logger);
            }
        }

        /// <summary>
        ///     Retrieve a list of default channel associations.
        /// </summary>
        /// <param name="repository">Optionally filter by repository</param>
        /// <param name="branch">Optionally filter by branch</param>
        /// <param name="channel">Optionally filter by channel</param>
        /// <returns>Collection of default channels.</returns>
        public Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(
            string repository = null,
            string branch = null,
            string channel = null)
        {
            CheckForValidBarClient();
            return _barClient.GetDefaultChannelsAsync(repository: repository,
                                                      branch: branch,
                                                      channel: channel);
        }

        /// <summary>
        ///     Adds a default channel association.
        /// </summary>
        /// <param name="repository">Repository receiving the default association</param>
        /// <param name="branch">Branch receiving the default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' should automatically be applied to.</param>
        /// <returns>Async task.</returns>
        public Task AddDefaultChannelAsync(string repository, string branch, string channel)
        {
            CheckForValidBarClient();
            return _barClient.AddDefaultChannelAsync(repository, branch, channel);
        }

        /// <summary>
        ///     Removes a default channel by id
        /// </summary>
        /// <param name="id">Id of default channel.</param>
        /// <returns>Async task</returns>
        public Task DeleteDefaultChannelAsync(int id)
        {
            CheckForValidBarClient();
            return _barClient.DeleteDefaultChannelAsync(id);
        }

        /// <summary>
        ///     Updates a default channel with new information.
        /// </summary>
        /// <param name="id">Id of default channel.</param>
        /// <param name="repository">New repository</param>
        /// <param name="branch">New branch</param>
        /// <param name="channel">New channel</param>
        /// <param name="enabled">Enabled/disabled status</param>
        /// <returns>Async task</returns>
        public Task UpdateDefaultChannelAsync(int id, string repository = null, string branch = null, string channel = null, bool? enabled = null)
        {
            CheckForValidBarClient();
            return _barClient.UpdateDefaultChannelAsync(id, repository, branch, channel, enabled);
        }

        /// <summary>
        ///     Creates a new channel in the Build Asset Registry
        /// </summary>
        /// <param name="name">Name of channel</param>
        /// <param name="classification">Classification of the channel</param>
        /// <returns>Newly created channel</returns>
        public Task<Channel> CreateChannelAsync(string name, string classification)
        {
            CheckForValidBarClient();
            return _barClient.CreateChannelAsync(name, classification);
        }

        /// <summary>
        /// Deletes a channel from the Build Asset Registry
        /// </summary>
        /// <param name="name">Name of channel</param>
        /// <returns>Channel just deleted</returns>
        public Task<Channel> DeleteChannelAsync(int id)
        {
            CheckForValidBarClient();
            return _barClient.DeleteChannelAsync(id);
        }

        /// <summary>
        ///     Get a set of subscriptions based on input filters.
        /// </summary>
        /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
        /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
        /// <param name="channelId">Filter by the source channel id of the subscription.</param>
        /// <returns>Set of subscription.</returns>
        public Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
            string sourceRepo = null,
            string targetRepo = null,
            int? channelId = null)
        {
            CheckForValidBarClient();
            return _barClient.GetSubscriptionsAsync(sourceRepo: sourceRepo,
                                                    targetRepo: targetRepo,
                                                    channelId: channelId);
        }

        /// <summary>
        ///     Retrieve a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">Id of subscription</param>
        /// <returns>Subscription information</returns>
        public Task<Subscription> GetSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return _barClient.GetSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Get repository merge policies
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Repository branch</param>
        /// <returns>List of merge policies</returns>
        public Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(string repoUri, string branch)
        {
            CheckForValidBarClient();
            return _barClient.GetRepositoryMergePoliciesAsync(repoUri, branch);
        }

        /// <summary>
        ///     Get a list of repository+branch combos and their associated merge policies.
        /// </summary>
        /// <param name="repoUri">Optional repository</param>
        /// <param name="branch">Optional branch</param>
        /// <returns>List of repository+branch combos</returns>
        public Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string repoUri = null, string branch = null)
        {
            CheckForValidBarClient();
            return _barClient.GetRepositoriesAsync(repoUri, branch);
        }

        /// <summary>
        ///     Set the merge policies for batchable subscriptions applied to a specific repo and branch
        /// </summary>
        /// <param name="repoUri">Repository</param>
        /// <param name="branch">Branch</param>
        /// <param name="mergePolicies">Merge policies. May be empty.</param>
        /// <returns>Task</returns>
        public Task SetRepositoryMergePoliciesAsync(string repoUri, string branch, List<MergePolicy> mergePolicies)
        {
            CheckForValidBarClient();
            return _barClient.SetRepositoryMergePoliciesAsync(repoUri, branch, mergePolicies ?? new List<MergePolicy>());
        }

        /// <summary>
        /// Trigger a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <returns>Subscription just triggered.</returns>
        public async Task<Subscription> TriggerSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return await _barClient.TriggerSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Create a new subscription
        /// </summary>
        /// <param name="channelName">Name of source channel</param>
        /// <param name="sourceRepo">URL of source repository</param>
        /// <param name="targetRepo">URL of target repository where updates should be made</param>
        /// <param name="targetBranch">Name of target branch where updates should be made</param>
        /// <param name="updateFrequency">Frequency of updates, can be 'none', 'everyBuild', 'everyDay', 'twiceDaily', or 'everyWeek'</param>
        /// <param name="batchable">Is subscription batchable</param>
        /// <param name="mergePolicies">
        ///     Dictionary of merge policies. Each merge policy is a name of a policy with an associated blob
        ///     of metadata
        /// </param>
        /// <returns>Newly created subscription, if successful</returns>
        public Task<Subscription> CreateSubscriptionAsync(
            string channelName,
            string sourceRepo,
            string targetRepo,
            string targetBranch,
            string updateFrequency,
            bool batchable,
            List<MergePolicy> mergePolicies)
        {
            CheckForValidBarClient();
            return _barClient.CreateSubscriptionAsync(
                channelName,
                sourceRepo,
                targetRepo,
                targetBranch,
                updateFrequency,
                batchable,
                mergePolicies);
        }

        /// <summary>
        /// Convert subscription id to a Guid, throwing if not valid.
        /// </summary>
        /// <param name="subscriptionId">ID of subscription.</param>
        /// <returns>New guid</returns>
        private Guid GetSubscriptionGuid(string subscriptionId)
        {
            if (!Guid.TryParse(subscriptionId, out Guid subscriptionGuid))
            {
                throw new ArgumentException($"Subscription id '{subscriptionId}' is not a valid guid.");
            }
            return subscriptionGuid;
        }

        /// <summary>
        ///     Delete a subscription by id
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to delete</param>
        /// <returns>Information on deleted subscription</returns>
        public async Task<Subscription> DeleteSubscriptionAsync(string subscriptionId)
        {
            CheckForValidBarClient();
            return await _barClient.DeleteSubscriptionAsync(GetSubscriptionGuid(subscriptionId));
        }

        /// <summary>
        ///     Update an existing subscription
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to update</param>
        /// <param name="subscription">Subscription information</param>
        /// <returns>Updated subscription</returns>
        public Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdate subscription)
        {
            CheckForValidBarClient();
            return _barClient.UpdateSubscriptionAsync(GetSubscriptionGuid(subscriptionId), subscription);
        }

        public async Task CreateNewBranchAsync(string repoUri, string baseBranch, string newBranch)
        {
            await _gitClient.CreateBranchAsync(repoUri, newBranch, baseBranch);
        }

        public async Task DeleteBranchAsync(string repoUri, string branch)
        {
            using (_logger.BeginScope($"Deleting branch '{branch}' from repo '{repoUri}'"))
            {
                await _gitClient.DeleteBranchAsync(repoUri, branch);
            }
        }

        /// <summary>
        ///     Get a list of pull request checks.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <returns>List of pull request checks</returns>
        public async Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            using (_logger.BeginScope($"Getting status checks for pull request '{pullRequestUrl}'..."))
            {
                return await _gitClient.GetPullRequestChecksAsync(pullRequestUrl);
            }
        }

        /// <summary>
        ///     Get a list of pull request reviews.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <returns>List of pull request checks</returns>
        public async Task<IEnumerable<Review>> GetPullRequestReviewsAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            using (_logger.BeginScope($"Getting reviews for pull request '{pullRequestUrl}'..."))
            {
                return await _gitClient.GetPullRequestReviewsAsync(pullRequestUrl);
            }
        }

        public async Task<IEnumerable<int>> SearchPullRequestsAsync(
            string repoUri,
            string pullRequestBranch,
            PrStatus status,
            string keyword = null,
            string author = null)
        {
            CheckForValidGitClient();
            return await _gitClient.SearchPullRequestsAsync(repoUri, pullRequestBranch, status, keyword, author);
        }

        public Task CreateOrUpdatePullRequestStatusCommentAsync(string pullRequestUrl, string message)
        {
            CheckForValidGitClient();
            return _gitClient.CreateOrUpdatePullRequestCommentAsync(pullRequestUrl, message);
        }

        public async Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Getting the status of pull request '{pullRequestUrl}'...");

            PrStatus status = await _gitClient.GetPullRequestStatusAsync(pullRequestUrl);

            _logger.LogInformation($"Status of pull request '{pullRequestUrl}' is '{status}'");

            return status;
        }

        public Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
        {
            return _gitClient.UpdatePullRequestAsync(pullRequestUri, pullRequest);
        }

        public async Task MergePullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Merging pull request '{pullRequestUrl}'...");

            await _gitClient.MergePullRequestAsync(pullRequestUrl, parameters ?? new MergePullRequestParameters());

            _logger.LogInformation($"Merging pull request '{pullRequestUrl}' succeeded!");
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
        ///     Get updates required by coherency constraints.
        /// </summary>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <param name="remoteFactory">Remote factory for remote queries.</param>
        /// <returns>Dependencies with updates.</returns>
        public async Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            IEnumerable<DependencyDetail> dependencies,
            IRemoteFactory remoteFactory)
        {
            List<DependencyUpdate> toUpdate = new List<DependencyUpdate>();

            IEnumerable<DependencyDetail> leavesOfCoherencyTrees =
                CalculateLeavesOfCoherencyTrees(dependencies);

            if (!leavesOfCoherencyTrees.Any())
            {
                // Nothing to do.
                return toUpdate;
            }

            DependencyGraphBuildOptions dependencyGraphBuildOptions = new DependencyGraphBuildOptions()
            {
                IncludeToolset = true,
                LookupBuilds = true,
                NodeDiff = NodeDiff.None
            };

            // Now make a walk over coherent dependencies. Note that coherent dependencies could make
            // a chain (A->B->C). In all cases we need to walk to the head of the chain, keeping track
            // of all elements in the chain. Also note that we are walking all dependencies here, not
            // just those that match the incoming AssetData and aligning all of these based on the coherency data.
            Dictionary<string, DependencyGraphNode> nodeCache = new Dictionary<string, DependencyGraphNode>();
            HashSet<DependencyDetail> visited = new HashSet<DependencyDetail>();
            foreach (DependencyDetail dependency in leavesOfCoherencyTrees)
            {
                // Build the update list.
                // Walk to head of dependency tree, keeping track of elements along the way.
                // If we hit a pinned dependency in the walk, that means we can't move
                // the dependency and therefore it is effectively the "head" of the subtree.
                // We will still visit all the elements in the chain eventually in this algorithm:
                // Consider A->B(pinned)->C(pinned)->D.
                List<DependencyDetail> updateList = new List<DependencyDetail>();
                DependencyDetail currentDependency = dependency;
                while (!string.IsNullOrEmpty(currentDependency.CoherentParentDependencyName) && !currentDependency.Pinned)
                {
                    updateList.Add(currentDependency);
                    DependencyDetail parentCoherentDependency = dependencies.FirstOrDefault(d =>
                        d.Name.Equals(currentDependency.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase));
                    currentDependency = parentCoherentDependency ?? throw new DarcException($"Dependency {currentDependency.Name} has non-existent parent " +
                            $"dependency {currentDependency.CoherentParentDependencyName}");

                    // An interesting corner case develops here. If we have two dependency
                    // chains that have a common element in the middle of the chain, then we can end up updating the common
                    // elements more than once unnecessarily. For example, let's say we have two chains:
                    // A->B->C
                    // D->B->C
                    // The walk of the first chain item will update B based on C, then the second chain will also update B based on C.
                    // We can break out of the chain building if we see a node already visited.
                    // However, we should ensure that we get the updated version of the head of this chain, rather than
                    // the current version.
                    if (visited.Contains(currentDependency))
                    {
                        DependencyUpdate alreadyUpdated = toUpdate.FirstOrDefault(alreadyUpdatedDep => alreadyUpdatedDep.From == currentDependency);
                        if (alreadyUpdated != null)
                        {
                            currentDependency = alreadyUpdated.To;
                        }
                        break;
                    }
                }

                DependencyGraphNode rootNode = null;

                // Build the graph to find the assets if we don't have the root in the cache.
                // The graph build is automatically broken when
                // all the desired assets are found (breadth first search). This means the cache may or
                // may not contain a complete graph for a given node. So, we first search the cache for the desired assets,
                // then if not found (or if there was no cache), we then build the graph from that node.
                bool nodeFromCache = nodeCache.TryGetValue($"{currentDependency.RepoUri}@{currentDependency.Commit}", out rootNode);
                if (!nodeFromCache)
                {
                    _logger.LogInformation($"Node not found in cache, starting graph build at " +
                        $"{currentDependency.RepoUri}@{currentDependency.Commit}");
                    rootNode = await BuildGraphAtDependencyAsync(remoteFactory, currentDependency, updateList, nodeCache);
                }

                List<DependencyDetail> leftToFind = new List<DependencyDetail>(updateList);
                // Now do the lookup to find the element in the tree for each item in the update list
                foreach (DependencyDetail dependencyInUpdateChain in updateList)
                {
                    (Asset coherentAsset, Build buildForAsset) =
                        FindAssetInBuildTree(dependencyInUpdateChain.Name, rootNode);

                    // If we originally got the root node from the cache the graph may be incomplete.
                    // Rebuild to attempt to find all the assets we have left to find. If we still can't find, or if
                    // the root node did not come the cache, then we're in an invalid state.
                    if (coherentAsset == null && nodeFromCache)
                    {
                        _logger.LogInformation($"Asset {dependencyInUpdateChain.Name} was not found in cached graph, rebuilding from " +
                            $"{currentDependency.RepoUri}@{currentDependency.Commit}");
                        rootNode = await BuildGraphAtDependencyAsync(remoteFactory, currentDependency, leftToFind, nodeCache);
                        // And attempt to find again.
                        (coherentAsset, buildForAsset) =
                            FindAssetInBuildTree(dependencyInUpdateChain.Name, rootNode);
                    }

                    if (coherentAsset == null)
                    {
                        // This is an invalid state. We can't satisfy the
                        // constraints so they should either be removed or pinned.
                        throw new DarcException($"Unable to update {dependencyInUpdateChain.Name} to have coherency with " +
                            $"parent {dependencyInUpdateChain.CoherentParentDependencyName}. No matching asset found in tree. " +
                            $"Either remove the coherency attribute or mark as pinned.");
                    }
                    else
                    {
                        leftToFind.Remove(dependencyInUpdateChain);
                    }

                    string buildRepoUri = buildForAsset.GitHubRepository ?? buildForAsset.AzureDevOpsRepository;

                    if (dependencyInUpdateChain.Name == coherentAsset.Name &&
                        dependencyInUpdateChain.Version == coherentAsset.Version &&
                        dependencyInUpdateChain.Commit == buildForAsset.Commit &&
                        dependencyInUpdateChain.RepoUri == buildRepoUri)
                    {
                        continue;
                    }

                    DependencyDetail updatedDependency = new DependencyDetail(dependencyInUpdateChain)
                    {
                        Name = coherentAsset.Name,
                        Version = coherentAsset.Version,
                        RepoUri = buildRepoUri,
                        Commit = buildForAsset.Commit,
                        Locations = coherentAsset.Locations?.Select(l => l.Location)
                    };

                    toUpdate.Add(new DependencyUpdate
                    {
                        From = dependencyInUpdateChain,
                        To = updatedDependency
                    });

                    visited.Add(dependencyInUpdateChain);
                }
            }

            return toUpdate;
        }

        private async Task<DependencyGraphNode> BuildGraphAtDependencyAsync(
            IRemoteFactory remoteFactory,
            DependencyDetail rootDependency,
            List<DependencyDetail> updateList,
            Dictionary<string, DependencyGraphNode> nodeCache)
        {
            DependencyGraphBuildOptions dependencyGraphBuildOptions = new DependencyGraphBuildOptions()
            {
                IncludeToolset = true,
                LookupBuilds = true,
                NodeDiff = NodeDiff.None,
                EarlyBuildBreak = new EarlyBreakOn
                {
                    Type = EarlyBreakOnType.Assets,
                    BreakOn = new List<string>(updateList.Select(d => d.Name))
                }
            };

            DependencyGraph dependencyGraph = await DependencyGraph.BuildRemoteDependencyGraphAsync(
                    remoteFactory, null, rootDependency.RepoUri, rootDependency.Commit, dependencyGraphBuildOptions, _logger);

            // Cache all nodes in this built graph.
            foreach (DependencyGraphNode node in dependencyGraph.Nodes)
            {
                if (!nodeCache.ContainsKey($"{node.Repository}@{node.Commit}"))
                {
                    nodeCache.Add($"{node.Repository}@{node.Commit}", node);
                }
            }

            return dependencyGraph.Root;
        }

        /// <summary>
        ///     Given an asset name, find the asset in the dependency tree.
        ///     Returns the asset with the shortest path to the root node.
        /// </summary>
        /// <param name="assetName">Name of asset.</param>
        /// <param name="currentNode">Dependency graph node to find the asset in.</param>
        /// <returns>(Asset, Build, depth), or (null, null, maxint) if not found.</returns>
        private (Asset asset, Build build, int buildDepth) FindAssetInBuildTree(string assetName, DependencyGraphNode currentNode, int currentDepth)
        {
            foreach (Build build in currentNode.ContributingBuilds)
            {
                Asset matchingAsset = build.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
                if (matchingAsset != null)
                {
                    return (matchingAsset, build, currentDepth);
                }
            }

            // Walk child nodes
            Asset shallowestAsset = null;
            Build shallowestBuild = null;
            int shallowestBuildDepth = int.MaxValue;
            foreach (DependencyGraphNode childNode in currentNode.Children)
            {
                (Asset asset, Build build, int buildDepth) = FindAssetInBuildTree(assetName, childNode, currentDepth + 1);
                if (asset != null)
                {
                    if (buildDepth < shallowestBuildDepth)
                    {
                        shallowestAsset = asset;
                        shallowestBuild = build;
                        shallowestBuildDepth = buildDepth;
                    }
                }
            }
            return (shallowestAsset, shallowestBuild, shallowestBuildDepth);
        }

        /// <summary>
        ///     Given an asset name, find the asset in the dependency tree.
        ///     Returns the asset with the shortest path to the root node.
        /// </summary>
        /// <param name="assetName">Name of asset.</param>
        /// <param name="currentNode">Dependency graph node to find the asset in.</param>
        /// <returns>(Asset, Build), or (null, null) if not found.</returns>
        private (Asset asset, Build build) FindAssetInBuildTree(string assetName, DependencyGraphNode currentNode)
        {
            (Asset asset, Build build, int depth) = FindAssetInBuildTree(assetName, currentNode, 0);
            return (asset, build);
        }

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
            Dictionary<DependencyDetail, DependencyDetail> toUpdate = new Dictionary<DependencyDetail, DependencyDetail>();

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

                DependencyDetail newDependency = new DependencyDetail(matchingDependencyByName)
                {
                    Commit = sourceCommit,
                    RepoUri = sourceRepoUri,
                    Version = asset.Version,
                    Name = asset.Name,
                    Locations = asset.Locations?.Select(l => l.Location)
                };

                toUpdate.Add(matchingDependencyByName, newDependency);
            }

            List<DependencyUpdate> dependencyUpdates = toUpdate.Select(kv => new DependencyUpdate
            {
                From = kv.Key,
                To = kv.Value
            }).ToList();

            return Task.FromResult(dependencyUpdates);
        }

        /// <summary>
        ///     Determine what dependencies need to be updated given an input repo uri, branch
        ///     and set of produced assets.
        /// </summary>
        /// <param name="repoUri">Repository</param>
        /// <param name="branch">Branch</param>
        /// <param name="sourceRepoUri">Repository the assets come from.</param>
        /// <param name="sourceCommit">Commit the assets come from.</param>
        /// <param name="assets">Assets as inputs for the update.</param>
        /// <returns>Map of existing dependency->updated dependency</returns>
        public async Task<List<DependencyUpdate>> GetRequiredNonCoherencyUpdatesAsync(
            string repoUri,
            string branch,
            string sourceRepoUri,
            string sourceCommit,
            IEnumerable<AssetData> assets)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Check if repo '{repoUri}' and branch '{branch}' needs updates...");

            IEnumerable<DependencyDetail> dependencyDetails =
                await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branch);
            return await GetRequiredNonCoherencyUpdatesAsync(sourceRepoUri, sourceCommit, assets, dependencyDetails);
        }

        /// <summary>
        ///     Given a repo and branch, determine what updates are required to satisfy
        ///     coherency constraints.
        /// </summary>
        /// <param name="repoUri">Repository uri to check for updates in.</param>
        /// <param name="branch">Branch to check for updates in.</param>
        /// <param name="remoteFactory">Remote factory use in walking the repo dependency graph.</param>
        /// <returns>List of dependencies requiring updates (with updated info).</returns>
        public async Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            string repoUri,
            string branch,
            IRemoteFactory remoteFactory)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Determining required coherency updates in {repoUri}@{branch}...");

            IEnumerable<DependencyDetail> currentDependencies =
                await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branch);
            return await GetRequiredCoherencyUpdatesAsync(currentDependencies, remoteFactory);
        }

        public async Task CommitUpdatesAsync(
            string repoUri,
            string branch,
            List<DependencyDetail> itemsToUpdate,
            string message)
        {
            IEnumerable<DependencyDetail> oldDependencies = await GetDependenciesAsync(repoUri, branch, loadAssetLocations: true);
            await AddAssetLocationToDependenciesAsync(itemsToUpdate);

            CheckForValidGitClient();
            GitFileContentContainer fileContainer =
                await _fileManager.UpdateDependencyFiles(itemsToUpdate, repoUri, branch, oldDependencies);
            List<GitFile> filesToCommit = new List<GitFile>();

            // If we are updating the arcade sdk we need to update the eng/common files
            // and the sdk versions in global.json
            DependencyDetail arcadeItem = itemsToUpdate.FirstOrDefault(
                i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));

            if (arcadeItem != null && repoUri != arcadeItem.RepoUri)
            {
                // Files in arcade repository. All Arcade items have a GitHub repo URI by default so we need to change the
                // URI from we are getting the eng/common files. If in an AzDO context we change the URI to that of
                // dotnet-arcade in dnceng

                string arcadeRepoUri = arcadeItem.RepoUri;

                if (Uri.TryCreate(repoUri, UriKind.Absolute, out Uri parsedUri))
                {
                    if (parsedUri.Host == "dev.azure.com" || parsedUri.Host.EndsWith("visualstudio.com"))
                    {
                        arcadeRepoUri = "https://dev.azure.com/dnceng/internal/_git/dotnet-arcade";
                    }
                }

                SemanticVersion arcadeDotnetVersion = await GetToolsDotnetVersionAsync(arcadeRepoUri, arcadeItem.Commit);
                if (arcadeDotnetVersion != null)
                {
                    fileContainer.GlobalJson = UpdateDotnetVersionGlobalJson(arcadeDotnetVersion, fileContainer.GlobalJson);
                }

                List<GitFile> engCommonFiles = await GetCommonScriptFilesAsync(arcadeRepoUri, arcadeItem.Commit);
                filesToCommit.AddRange(engCommonFiles);

                // Files in the target repo
                string latestCommit = await _gitClient.GetLastCommitShaAsync(repoUri, branch);
                List<GitFile> targetEngCommonFiles = await GetCommonScriptFilesAsync(repoUri, latestCommit);

                var deletedFiles = new List<string>();

                foreach (GitFile file in targetEngCommonFiles)
                {
                    if (!engCommonFiles.Where(f => f.FilePath == file.FilePath).Any())
                    {
                        deletedFiles.Add(file.FilePath);
                        // This is a file in the repo's eng/common folder that isn't present in Arcade at the
                        // requested SHA so delete it during the update.
                        // GitFile instances do not have public setters since we insert/retrieve them from an
                        // In-memory cache and we don't want anything to modify the cached references,
                        // so add a copy with a Delete FileOperation.
                        filesToCommit.Add(new GitFile(
                                file.FilePath,
                                file.Content,
                                file.ContentEncoding,
                                file.Mode,
                                GitFileOperation.Delete));
                    }
                }

                if (deletedFiles.Count > 0)
                {
                    _logger.LogInformation($"Dependency update from Arcade commit {arcadeItem.Commit} to {repoUri} " +
                        $"on branch {branch}@{latestCommit} will delete files in eng/common." +
                        $" Source file count: {engCommonFiles.Count}, Target file count: {targetEngCommonFiles.Count}." +
                        $" Deleted files: {String.Join(Environment.NewLine, deletedFiles)}");
                }
            }

            filesToCommit.AddRange(fileContainer.GetFilesToCommit());

            await _gitClient.CommitFilesAsync(filesToCommit, repoUri, branch, message);
        }

        public Task<PullRequest> GetPullRequestAsync(string pullRequestUri)
        {
            CheckForValidGitClient();
            return _gitClient.GetPullRequestAsync(pullRequestUri);
        }

        public Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
        {
            CheckForValidGitClient();
            return _gitClient.CreatePullRequestAsync(repoUri, pullRequest);
        }

        /// <summary>
        ///     Diff two commits in a repository and return information about them.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="baseVersion">Base version</param>
        /// <param name="targetVersion">Target version</param>
        /// <returns>Diff information</returns>
        public async Task<GitDiff> GitDiffAsync(string repoUri, string baseVersion, string targetVersion)
        {
            CheckForValidGitClient();

            // If base and target are the same, return no diff
            if (baseVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                return GitDiff.NoDiff(baseVersion);
            }

            return await _gitClient.GitDiffAsync(repoUri, baseVersion, targetVersion);
        }

        /// <summary>
        /// Checks that a repository exists
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <returns>True if the repository exists, false otherwise.</returns>
        public async Task<bool> RepositoryExistsAsync(string repoUri)
        {
            CheckForValidGitClient();

            return await _gitClient.RepoExistsAsync(repoUri);
        }

        /// <summary>
        ///     Get the latest commit in a branch
        /// </summary>
        /// <param name="repoUri">Remote repository</param>
        /// <param name="branch">Branch</param>
        /// <returns>Latest commit</returns>
        public Task<string> GetLatestCommitAsync(string repoUri, string branch)
        {
            CheckForValidGitClient();
            return _gitClient.GetLastCommitShaAsync(repoUri, branch);
        }

        /// <summary>
        ///     Retrieve the list of channels from the build asset registry.
        /// </summary>
        /// <param name="classification">Optional classification to get</param>
        /// <returns></returns>
        public Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null)
        {
            CheckForValidBarClient();
            return _barClient.GetChannelsAsync(classification);
        }

        /// <summary>
        ///     Retrieve a specific channel by name.
        /// </summary>
        /// <param name="channel">Channel name.</param>
        /// <returns>Channel or null if not found.</returns>
        public Task<Channel> GetChannelAsync(string channel)
        {
            CheckForValidBarClient();
            return _barClient.GetChannelAsync(channel);
        }

        /// <summary>
        ///     Retrieve a specific channel by id.
        /// </summary>
        /// <param name="channel">Channel id.</param>
        /// <returns>Channel or null if not found.</returns>
        public Task<Channel> GetChannelAsync(int channel)
        {
            CheckForValidBarClient();
            return _barClient.GetChannelAsync(channel);
        }

        /// <summary>
        ///     Retrieve the latest build of a repository on a specific channel.
        /// </summary>
        /// <param name="repoUri">URI of repository to obtain a build for.</param>
        /// <param name="channelId">Channel the build was applied to.</param>
        /// <returns>Latest build of <paramref name="repoUri"/> on channel <paramref name="channelId"/>,
        /// or null if there is no latest.</returns>
        /// <remarks>The build's assets are returned</remarks>
        public async Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
        {
            CheckForValidBarClient();
            try
            {
                return await _barClient.GetLatestBuildAsync(repoUri: repoUri, channelId: channelId);
            }
            catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>
        ///     Assign a particular build to a channel
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <param name="channelId">Channel id</param>
        /// <returns>Async task</returns>
        public Task AssignBuildToChannel(int buildId, int channelId)
        {
            CheckForValidBarClient();
            return _barClient.AssignBuildToChannel(buildId, channelId);
        }

        /// <summary>
        ///     Retrieve information about the specified build.
        /// </summary>
        /// <param name="buildId">Id of build.</param>
        /// <returns>Information about the specific build</returns>
        /// <remarks>The build's assets are returned</remarks>
        public Task<Build> GetBuildAsync(int buildId)
        {
            CheckForValidBarClient();
            return _barClient.GetBuildAsync(buildId);
        }

        /// <summary>
        ///     Get a list of builds for the given repo uri and commit.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Commit</param>
        /// <returns></returns>
        public Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
        {
            CheckForValidBarClient();
            return _barClient.GetBuildsAsync(repoUri: repoUri,
                                             commit: commit);
        }

        /// <summary>
        ///     Get assets matching a particular set of properties. All are optional.
        /// </summary>
        /// <param name="name">Name of asset</param>
        /// <param name="version">Version of asset</param>
        /// <param name="buildId">ID of build producing the asset</param>
        /// <param name="nonShipping">Only non-shipping</param>
        /// <returns>List of assets.</returns>
        public Task<IEnumerable<Asset>> GetAssetsAsync(string name = null,
                                                       string version = null,
                                                       int? buildId = null,
                                                       bool? nonShipping = null)
        {
            CheckForValidBarClient();
            return _barClient.GetAssetsAsync(name: name,
                                      version: version,
                                      buildId: buildId,
                                      nonShipping: nonShipping);
        }

        /// <summary>
        ///     Get the list of dependencies in the specified repo and branch/commit
        /// </summary>
        /// <param name="repoUri">Repository to get dependencies from</param>
        /// <param name="branchOrCommit">Commit to get dependencies at</param>
        /// <param name="name">Optional name of specific dependency to get information on</param>
        /// <param name="loadAssetLocations">Optional switch to include the asset locations</param>
        /// <returns>Matching dependency information.</returns>
        public async Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri,
                                                                              string branchOrCommit,
                                                                              string name = null,
                                                                              bool loadAssetLocations = false)
        {
            CheckForValidGitClient();
            var dependencies = (await _fileManager.ParseVersionDetailsXmlAsync(repoUri, branchOrCommit)).Where(
                dependency => string.IsNullOrEmpty(name) || dependency.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (loadAssetLocations)
            {
                await AddAssetLocationToDependenciesAsync(dependencies);
            }

            return dependencies;
        }

        /// <summary>
        ///     Clone a remote repo
        /// </summary>
        /// <param name="repoUri">Repository to clone</param>
        /// <param name="commit">Branch, commit, or tag to checkout</param>
        /// <param name="targetDirectory">Directory to clone to</param>
        /// <param name="gitDirectory">Location for the .git directory</param>
        /// <returns></returns>
        public void Clone(string repoUri, string commit, string targetDirectory, string gitDirectory = null)
        {
            CheckForValidGitClient();
            _gitClient.Clone(repoUri, commit, targetDirectory, gitDirectory);
        }

        /// <summary>
        ///     Called prior to operations requiring the BAR.  Throws if a bar client isn't available.
        /// </summary>
        private void CheckForValidBarClient()
        {
            if (_barClient == null)
            {
                throw new ArgumentException("Must supply a build asset registry client");
            }
        }

        /// <summary>
        ///     Called prior to operations requiring the BAR.  Throws if a git client isn't available;
        /// </summary>
        private void CheckForValidGitClient()
        {
            if (_gitClient == null)
            {
                throw new ArgumentException("Must supply a valid GitHub/Azure DevOps PAT");
            }
        }

        public async Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit)
        {
            CheckForValidGitClient();
            _logger.LogInformation("Generating commits for script files");

            List<GitFile> files = await _gitClient.GetFilesAtCommitAsync(repoUri, commit, "eng/common");

            _logger.LogInformation("Generating commits for script files succeeded!");

            return files;
        }

        /// <summary>
        /// Get the tools.dotnet section of the global.json from a target repo URI
        /// </summary>
        /// <param name="repoUri">repo to get the version from</param>
        /// <param name="commit">commit sha to query</param>
        /// <returns></returns>
        private async Task<SemanticVersion> GetToolsDotnetVersionAsync(string repoUri, string commit)
        {
            CheckForValidGitClient();
            _logger.LogInformation("Reading dotnet version from global.json");

            JObject globalJson = await _fileManager.ReadGlobalJsonAsync(repoUri, commit);
            JToken dotnet = globalJson.SelectToken("tools.dotnet", true);

            _logger.LogInformation("Reading dotnet version from global.json succeeded!");

            SemanticVersion.TryParse(dotnet.ToString(), out SemanticVersion dotnetVersion);

            if (dotnetVersion == null)
            {
                _logger.LogError($"Failed to parse dotnet version from global.json from repo: {repoUri} at commit {commit}. Version: {dotnet.ToString()}");
            }

            return dotnetVersion;
        }

        /// <summary>
        /// Updates the global.json entries for tools.dotnet and sdk.version if they are older than an incoming version
        /// </summary>
        /// <param name="incomingDotnetVersion">version to compare against</param>
        /// <param name="repoGlobalJson">Global.Json file to update</param>
        /// <returns>Updated global.json file if was able to update, or the unchanged global.json if unable to</returns>
        private GitFile UpdateDotnetVersionGlobalJson(SemanticVersion incomingDotnetVersion, GitFile repoGlobalJson)
        {
            string repoGlobalJsonContent = repoGlobalJson.ContentEncoding == ContentEncoding.Base64 ?
                _gitClient.GetDecodedContent(repoGlobalJson.Content) :
                repoGlobalJson.Content;
            try
            {
                JObject parsedGlobalJson = JObject.Parse(repoGlobalJsonContent);
                if (SemanticVersion.TryParse(parsedGlobalJson.SelectToken("tools.dotnet").ToString(), out SemanticVersion repoDotnetVersion))
                {
                    if (repoDotnetVersion.CompareTo(incomingDotnetVersion) < 0)
                    {
                        parsedGlobalJson["tools"]["dotnet"] = incomingDotnetVersion.ToNormalizedString();

                        // Also update and keep sdk.version in sync.
                        JToken sdkVersion = parsedGlobalJson.SelectToken("sdk.version");
                        if (sdkVersion != null)
                        {
                            parsedGlobalJson["sdk"]["version"] = incomingDotnetVersion.ToNormalizedString();
                        }
                        return new GitFile(VersionFiles.GlobalJson, parsedGlobalJson);
                    }
                    return repoGlobalJson;
                }
                else
                {
                    _logger.LogError("Could not parse the repo's dotnet version from the global.json. Skipping update to dotnet version sections");
                    return repoGlobalJson;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Dotnet version for global.json. Skipping update to version sections.");
                return repoGlobalJson;
            }

        }

        /// <summary>
        ///     Update a list of dependencies with asset locations.
        /// </summary>
        /// <param name="dependencies">Dependencies to load locations for</param>
        /// <returns>Async task</returns>
        public async Task AddAssetLocationToDependenciesAsync(IEnumerable<DependencyDetail> dependencies)
        {
            foreach (var dependency in dependencies)
            {
                Dictionary<int, Build> buildCache = new Dictionary<int, Build>();
                IEnumerable<Asset> matchingAssets = await GetAssetsAsync(dependency.Name, dependency.Version);
                List<Asset> matchingAssetsFromSameSha = new List<Asset>();

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
                    IEnumerable<String> currentAssetLocations = latestAsset.Locations?
                        .Where(l=>l.Type == LocationType.NugetFeed)
                        .Select(l => l.Location);

                    if (currentAssetLocations == null)
                    {
                        continue;
                    }

                    dependency.Locations = currentAssetLocations;
                }
            }
        }

        /// <summary>
        ///     Update an existing build.
        /// </summary>
        /// <param name="buildId">Build to update</param>
        /// <param name="buildUpdate">Updated build info</param>
        /// <returns>Updated build</returns>
        public Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate)
        {
            CheckForValidBarClient();
            return _barClient.UpdateBuildAsync(buildId, buildUpdate);
        }

        /// <summary>
        ///  Creates a new goal or updates the existing goal (in minutes) for a Defintion in a Channel.
        /// </summary>
        /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
        /// <param name="definitionId">Azure DevOps DefinitionId.</param>
        /// <param name="minutes">Goal in minutes for a Definition in a Channel.</param>
        /// <returns>Async task.</returns>
        public Task<Goal> SetGoalAsync(string channel, int definitionId, int minutes)
        {
            CheckForValidBarClient();
            return _barClient.SetGoalAsync(channel, definitionId, minutes);
        }

        /// <summary>
        ///     Gets goal (in minutes) for a Defintion in a Channel.
        /// </summary>
        /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
        /// <param name="definitionId">Azure DevOps DefinitionId.</param>
        /// <returns>Returns Goal in minutes.</returns>
        public Task<Goal> GetGoalAsync(string channel, int definitionId)
        {
            CheckForValidBarClient();
            return _barClient.GetGoalAsync(channel, definitionId);
        }
    }
}
