// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Maestro.Contracts;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using Asset = Microsoft.DotNet.Maestro.Client.Models.Asset;
using Subscription = Microsoft.DotNet.Maestro.Client.Models.Subscription;

namespace Microsoft.DotNet.DarcLib
{
    public sealed class Remote : IRemote
    {
        private readonly IBarClient _barClient;
        private readonly GitFileManager _fileManager;
        private readonly IGitRepo _gitClient;
        private readonly ILogger _logger;

        //[DependencyUpdate]: <> (Begin)
        //- **Updates**:
        //- **Foo**: from to 1.2.0
        //- **Bar**: from to 2.2.0
        //[DependencyUpdate]: <> (End)
        private static readonly Regex DependencyUpdatesPattern =
            new Regex(@"\[DependencyUpdate\]: <> \(Begin\)([^\[]+)\[DependencyUpdate\]: <> \(End\)");

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
            _logger.LogInformation($"Deleting branch '{branch}' from repo '{repoUri}'");
            await _gitClient.DeleteBranchAsync(repoUri, branch);
        }

        /// <summary>
        ///     Get a list of pull request checks.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <returns>List of pull request checks</returns>
        public async Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Getting status checks for pull request '{pullRequestUrl}'...");
            return await _gitClient.GetPullRequestChecksAsync(pullRequestUrl);
        }

        /// <summary>
        ///     Get a list of pull request reviews.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request</param>
        /// <returns>List of pull request checks</returns>
        public async Task<IEnumerable<Review>> GetPullRequestReviewsAsync(string pullRequestUrl)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Getting reviews for pull request '{pullRequestUrl}'...");
            return await _gitClient.GetPullRequestReviewsAsync(pullRequestUrl);
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

        public Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations)
        {
            CheckForValidGitClient();
            return _gitClient.CreateOrUpdatePullRequestMergeStatusInfoAsync(pullRequestUrl, evaluations);
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

        /// <summary>
        ///     Delete a Pull Request branch
        /// </summary>
        /// <param name="pullRequestUri">URI of pull request to delete branch for</param>
        /// <returns>Async task</returns>
        public async Task DeletePullRequestBranchAsync(string pullRequestUri)
        {
            try
            {
                await _gitClient.DeletePullRequestBranchAsync(pullRequestUri);
            }
            catch (Exception e)
            {
                throw new DarcException("Failed to delete head branch for pull request {pullRequestUri}", e);
            }
        }

        /// <summary>
        /// Merges pull request for a dependency update  
        /// </summary>
        /// <param name="pullRequestUrl"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public async Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters)
        {
            CheckForValidGitClient();
            _logger.LogInformation($"Merging pull request '{pullRequestUrl}'...");

            var pr = await _gitClient.GetPullRequestAsync(pullRequestUrl);
            var dependencyUpdate = DependencyUpdatesPattern.Matches(pr.Description).Select(x => x.Groups[1].Value.Trim().Replace("*", string.Empty));
            string commitMessage = $@"{pr.Title}
{string.Join("\r\n\r\n", dependencyUpdate)}";
            var commits = await _gitClient.GetPullRequestCommitsAsync(pullRequestUrl);
            foreach (Commit commit in commits)
            {
                if (!commit.Author.Equals("dotnet-maestro[bot]"))
                {
                    commitMessage += $@"

 - {commit.Message}";
                }
            }

            await _gitClient.MergeDependencyPullRequestAsync(pullRequestUrl,
                    parameters ?? new MergePullRequestParameters(), commitMessage);

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
            List<DependencyUpdate> toUpdate = new List<DependencyUpdate>();
            IEnumerable<DependencyDetail> leavesOfCoherencyTrees = CalculateLeavesOfCoherencyTrees(dependencies);

            if (!leavesOfCoherencyTrees.Any())
            {
                // Nothing to do.
                return toUpdate;
            }

            List<CoherencyError> coherencyErrors = new List<CoherencyError>();

            // Cache of dependencies. Key is "<repo>@<sha>".
            Dictionary<string, IEnumerable<DependencyDetail>> dependenciesCache =
                new Dictionary<string, IEnumerable<DependencyDetail>>();
            // Cache of builds with assets. Key is "<repo>@<sha>".
            Dictionary<string, List<Build>> buildCache = new Dictionary<string, List<Build>>();
            // Cache of nuget config files for further build disambiguation. Key is "<repo>@<sha>".
            Dictionary<string, IEnumerable<string>> nugetConfigCache = new Dictionary<string, IEnumerable<string>>();

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
                Stack<DependencyDetail> updateStack = new Stack<DependencyDetail>();
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
                    DependencyDetail parentCoherentDependency = toUpdate.FirstOrDefault(d =>
                        d.To.Name.Equals(dependencyToUpdate.CoherentParentDependencyName, StringComparison.OrdinalIgnoreCase))?.To;

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

                    string parentCoherentDependencyCacheKey = $"{parentCoherentDependency.RepoUri}@{parentCoherentDependency.Commit}";

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
                        coherencyErrors.Add(new CoherencyError()
                        {
                            Dependency = dependencyToUpdate,
                            Error = $"{parentCoherentDependency.RepoUri} @ {parentCoherentDependency.Commit} does not contain dependency {dependencyToUpdate.Name}",
                            PotentialSolutions = new List<string> {
                                $"Add the dependency to {parentCoherentDependency.RepoUri}.",
                                $"Pin the dependenency.",
                                "Remove the CoherentParentDependency attribute."
                            }
                        });

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

                    _logger.LogInformation($"Dependency {dependencyToUpdate} will be updated to " +
                        $"{cpdDependency.Version} from {cpdDependency.RepoUri}@{cpdDependency.Commit}.");

                    Asset coherentAsset = await DisambiguateAssetsAsync(remoteFactory, buildCache, nugetConfigCache,
                        parentCoherentDependency, cpdDependency);

                    DependencyDetail updatedDependency = new DependencyDetail(dependencyToUpdate)
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
                throw new DarcCoherencyException(coherencyErrors);
            }

            return toUpdate;
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
            string parentCoherentDependencyCacheKey = $"{parentCoherentDependency.RepoUri}@{parentCoherentDependency.Commit}";

            _logger.LogInformation($"Attempting to disambiguate {cpdDependency.Name}@{cpdDependency.Version} " +
                $"based on nuget.config at {parentCoherentDependencyCacheKey}");

            AssetComparer assetComparer = new AssetComparer();

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
                List<Build> buildsWithMatchingAssets = potentialBuilds.Where(
                    build => build.Assets.Any(asset => assetComparer.Equals(asset, cpdDependency))).OrderByDescending(build => build.Id).ToList();
                List<Asset> allMatchingAssets = buildsWithMatchingAssets.Select(build => build.Assets.FirstOrDefault(
                    asset => assetComparer.Equals(asset, cpdDependency))).ToList();

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
                    XmlDocument nugetConfig = await _fileManager.ReadNugetConfigAsync(parentCoherentDependency.RepoUri, parentCoherentDependency.Commit);
                    nugetFeeds = _fileManager.GetPackageSources(nugetConfig).Select(nameAndFeed => nameAndFeed.feed);

                    nugetConfigCache.Add(parentCoherentDependencyCacheKey, nugetFeeds);
                }

                // Find assets with locations that match any feed in the nuget.config file.
                var assetsWithMatchingLocations = allMatchingAssets.Where(asset =>
                {
                    if (asset.Locations != null)
                    {
                        return asset.Locations.Select(location => location.Location).Intersect(nugetFeeds).Any();
                    }
                    return false;
                }).ToList();

                if (assetsWithMatchingLocations.Count != 1)
                {
                    // Find the newest build in the matching assets
                    return buildsWithMatchingAssets.First().Assets.FirstOrDefault(
                        asset => assetComparer.Equals(asset, cpdDependency));
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

        /// <summary>
        ///     Get updates required by coherency constraints.
        /// </summary>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <param name="remoteFactory">Remote factory for remote queries.</param>
        /// <returns>Dependencies with updates.</returns>
        private async Task<List<DependencyUpdate>> GetRequiredLegacyCoherencyUpdatesAsync(
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

                // Now do the lookup to find the element in the tree for each item in the update list
                foreach (DependencyDetail dependencyInUpdateChain in updateList)
                {
                    (Asset coherentAsset, Build buildForAsset) =
                        FindNewestAssetInBuildTree(dependencyInUpdateChain.Name, rootNode);

                    if (coherentAsset == null)
                    {
                        // This is an invalid state. We can't satisfy the
                        // constraints so they should either be removed or pinned.
                        throw new DarcCoherencyException(new CoherencyError()
                        {
                            Dependency = dependencyInUpdateChain,
                            Error = $"No matching build asset found in dependency graph under {currentDependency.RepoUri} @ {currentDependency.Commit}",
                            PotentialSolutions = new List<string> {
                                $"Remove the coherency attribute",
                                $"Pin the dependenency.",
                            }
                        });
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
                NodeDiff = NodeDiff.None
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
        ///     Returns the newest asset in the tree.
        /// </summary>
        /// <param name="assetName">Name of asset.</param>
        /// <param name="currentNode">Dependency graph node to find the asset in.</param>
        /// <returns>(Asset, Build, depth), or (null, null, maxint) if not found.</returns>
        private (Asset asset, Build build) FindNewestAssetInBuildTree(string assetName, DependencyGraphNode currentNode, DateTimeOffset assetProductionTime)
        {
            Asset newestMatchingAsset = null;
            Build newestMatchingBuild = null;
            DateTimeOffset newestAssetProductionTime = assetProductionTime;
            foreach (Build build in currentNode.ContributingBuilds)
            {
                // If the contributing build is older than the current asset production time,
                // don't need to check here
                if (build.DateProduced.CompareTo(newestAssetProductionTime) < 0)
                {
                    continue;
                }
                
                Asset matchingAsset = build.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
                if (matchingAsset != null)
                {
                    newestMatchingAsset = matchingAsset;
                    newestMatchingBuild = build;
                    newestAssetProductionTime = build.DateProduced;
                }
            }

            // Walk child nodes
            foreach (DependencyGraphNode childNode in currentNode.Children)
            {
                (Asset asset, Build build) = FindNewestAssetInBuildTree(assetName, childNode, newestAssetProductionTime);
                if (asset != null)
                {
                    if (build.DateProduced.CompareTo(newestAssetProductionTime) > 0)
                    {
                        newestMatchingAsset = asset;
                        newestMatchingBuild = build;
                        newestAssetProductionTime = build.DateProduced;
                    }
                }
            }
            return (newestMatchingAsset, newestMatchingBuild);
        }

        /// <summary>
        ///     Given an asset name, find the asset in the dependency tree.
        ///     Returns the asset with the shortest path to the root node.
        /// </summary>
        /// <param name="assetName">Name of asset.</param>
        /// <param name="currentNode">Dependency graph node to find the asset in.</param>
        /// <returns>(Asset, Build), or (null, null) if not found.</returns>
        private (Asset asset, Build build) FindNewestAssetInBuildTree(string assetName, DependencyGraphNode currentNode)
        {
            (Asset asset, Build build) = FindNewestAssetInBuildTree(assetName, currentNode, DateTimeOffset.MinValue);
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
        ///     Get updates required by coherency constraints.
        /// </summary>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <param name="remoteFactory">Remote factory for remote queries.</param>
        /// <param name="coherencyMode">Coherency algorithm that should be used</param>
        /// <returns>List of dependency updates.</returns>
        public async Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            IEnumerable<DependencyDetail> dependencies,
            IRemoteFactory remoteFactory,
            CoherencyMode coherencyMode)
        {
            _logger.LogInformation($"Running coherency update using the {coherencyMode} algorithm");
            switch (coherencyMode)
            {
                case CoherencyMode.Strict:
                    return await GetRequiredStrictCoherencyUpdatesAsync(dependencies, remoteFactory);
                case CoherencyMode.Legacy:
                    return await GetRequiredLegacyCoherencyUpdatesAsync(dependencies, remoteFactory);
                default:
                    throw new NotImplementedException($"Coherency mode {coherencyMode} is not supported.");
            }
        }

        /// <summary>
        ///     Commit a set of updated dependencies to a repository
        /// </summary>
        /// <param name="repoUri">Repository to update</param>
        /// <param name="branch">Branch of <paramref name="repoUri"/> to update.</param>
        /// <param name="remoteFactory">Remote factory for obtaining common script files from arcade</param>
        /// <param name="itemsToUpdate">Dependencies that need updating.</param>
        /// <param name="message">Commit message.</param>
        /// <returns>Async task.</returns>
        public async Task<List<GitFile>> CommitUpdatesAsync(
            string repoUri,
            string branch,
            IRemoteFactory remoteFactory,
            List<DependencyDetail> itemsToUpdate,
            string message)
        {
            CheckForValidGitClient();

            IEnumerable<DependencyDetail> oldDependencies = await GetDependenciesAsync(repoUri, branch, loadAssetLocations: true);
            await AddAssetLocationToDependenciesAsync(itemsToUpdate);

            // If we are updating the arcade sdk we need to update the eng/common files
            // and the sdk versions in global.json
            DependencyDetail arcadeItem = itemsToUpdate.FirstOrDefault(
                i => string.Equals(i.Name, "Microsoft.DotNet.Arcade.Sdk", StringComparison.OrdinalIgnoreCase));
            
            SemanticVersion targetDotNetVersion = null;
            bool mayNeedArcadeUpdate = (arcadeItem != null && repoUri != arcadeItem.RepoUri);
            IRemote arcadeRemote = null;

            if (mayNeedArcadeUpdate)
            {
                arcadeRemote = await remoteFactory.GetRemoteAsync(arcadeItem.RepoUri, _logger);
                targetDotNetVersion = await arcadeRemote.GetToolsDotnetVersionAsync(arcadeItem.RepoUri, arcadeItem.Commit);
            }

            GitFileContentContainer fileContainer =
                await _fileManager.UpdateDependencyFiles(itemsToUpdate, repoUri, branch, oldDependencies, targetDotNetVersion);
            List<GitFile> filesToCommit = new List<GitFile>();

            if (mayNeedArcadeUpdate)
            {
                // Files in the source arcade repo. We use the remote factory because the
                // arcade repo may be in github while this remote is targeted at AzDO.
                List<GitFile> engCommonFiles = await arcadeRemote.GetCommonScriptFilesAsync(arcadeItem.RepoUri, arcadeItem.Commit);
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

            return filesToCommit;
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
            if (string.IsNullOrWhiteSpace(repoUri) || _gitClient == null)
            {
                return false;
            }

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
        public Task AssignBuildToChannelAsync(int buildId, int channelId)
        {
            CheckForValidBarClient();
            return _barClient.AssignBuildToChannelAsync(buildId, channelId);
        }

        /// <summary>
        ///     Remove a particular build from a channel
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <param name="channelId">Channel id</param>
        /// <returns>Async task</returns>
        public Task DeleteBuildFromChannelAsync(int buildId, int channelId)
        {
            CheckForValidBarClient();
            return _barClient.DeleteBuildFromChannelAsync(buildId, channelId);
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
        ///     Called prior to operations requiring GIT.  Throws if a git client isn't available;
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
        public async Task<SemanticVersion> GetToolsDotnetVersionAsync(string repoUri, string commit)
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

        /// <summary>
        ///     Gets official and pr build times (in minutes) for a default channel summarized over a number of days.
        /// </summary>
        /// <param name="defaultChannelId">Id of the default channel</param>
        /// <param name="days">Number of days to summarize over</param>
        /// <returns>Returns BuildTime in minutes</returns>
        public Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days)
        {
            CheckForValidBarClient();
            return _barClient.GetBuildTimeAsync(defaultChannelId, days);
        }
    }
}
