// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Maestro.Contracts;
using Microsoft.DotNet.Maestro.Client.Models;
using NuGet.Versioning;
using Asset = Microsoft.DotNet.Maestro.Client.Models.Asset;
using Subscription = Microsoft.DotNet.Maestro.Client.Models.Subscription;

namespace Microsoft.DotNet.DarcLib
{
    public interface IRemote
    {
        #region Channel Operations

        /// <summary>
        ///     Retrieve a set of default channel associations based on the provided filters.
        /// </summary>
        /// <param name="repository">Repository name</param>
        /// <param name="branch">Name of branch</param>
        /// <param name="channel">Channel name.</param>
        /// <returns>List of default channel associations. Channel is matched based on case insensitivity.</returns>
        Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(
            string repository = null,
            string branch = null,
            string channel = null);

        /// <summary>
        ///     Adds a default channel association.
        /// </summary>
        /// <param name="repository">Repository receiving the default association</param>
        /// <param name="branch">Branch receiving the default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' should automatically be applied to.</param>
        /// <returns>Async task.</returns>
        Task AddDefaultChannelAsync(string repository, string branch, string channel);

        /// <summary>
        ///     Removes a default channel by id
        /// </summary>
        /// <param name="id">Id of default channel.</param>
        /// <returns>Async task</returns>
        Task DeleteDefaultChannelAsync(int id);

        /// <summary>
        ///     Updates a default channel with new information.
        /// </summary>
        /// <param name="id">Id of default channel.</param>
        /// <param name="repository">New repository</param>
        /// <param name="branch">New branch</param>
        /// <param name="channel">New channel</param>
        /// <param name="enabled">Enabled/disabled status</param>
        /// <returns>Async task</returns>
        Task UpdateDefaultChannelAsync(int id, string repository = null, string branch = null, string channel = null, bool? enabled = null);

        /// <summary>
        ///     Create a new channel
        /// </summary>
        /// <param name="name">Name of channel. Must be unique.</param>
        /// <param name="classification">Classification of channel.</param>
        /// <returns>Newly created channel</returns>
        Task<Channel> CreateChannelAsync(string name, string classification);

        /// <summary>
        ///     Delete a channel.
        /// </summary>
        /// <param name="id">Id of channel to delete</param>
        /// <returns>Channel just deleted</returns>
        Task<Channel> DeleteChannelAsync(int id);

        /// <summary>
        ///     Retrieve the list of channels from the build asset registry.
        /// </summary>
        /// <param name="classification">Optional classification to get</param>
        /// <returns></returns>
        Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null);

        /// <summary>
        ///     Retrieve a specific channel by name.
        /// </summary>
        /// <param name="channel">Channel name.</param>
        /// <returns>Channel or null if not found.</returns>
        Task<Channel> GetChannelAsync(string channel);

        /// <summary>
        ///     Retrieve a specific channel by id.
        /// </summary>
        /// <param name="channel">Channel id.</param>
        /// <returns>Channel or null if not found.</returns>
        Task<Channel> GetChannelAsync(int channelId);

        #endregion

        #region Subscription Operations

        /// <summary>
        ///     Get a set of subscriptions based on input filters.
        /// </summary>
        /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
        /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
        /// <param name="channelId">Filter by the source channel id of the subscription.</param>
        /// <returns>Set of subscription.</returns>
        Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
            string sourceRepo = null,
            string targetRepo = null,
            int? channelId = null);

        /// <summary>
        ///     Retrieve a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">Id of subscription</param>
        /// <returns>Subscription information</returns>
        Task<Subscription> GetSubscriptionAsync(string subscriptionId);

        /// <summary>
        /// Trigger a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <returns>Subscription just triggered.</returns>
        Task<Subscription> TriggerSubscriptionAsync(string subscriptionId);

        /// <summary>
        /// Trigger a subscription by ID and source build id
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <param name="sourceBuildId">Bar ID of build to use (instead of latest)</param>
        /// <returns>Subscription just triggered.</returns>
        Task<Subscription> TriggerSubscriptionAsync(string subscriptionId, int sourceBuildId);

        /// <summary>
        ///     Create a new subscription.
        /// </summary>
        /// <param name="channelName">Name of source channel.</param>
        /// <param name="sourceRepo">Source repository URI.</param>
        /// <param name="targetRepo">Target repository URI.</param>
        /// <param name="targetBranch">Target branch in <paramref name="targetRepo"/></param>
        /// <param name="updateFrequency">Frequency of update.  'none', 'everyBuild', 'everyDay', 'twiceDaily', or 'everyWeek'.</param>
        /// <param name="batchable">If true, the subscription is batchable</param>
        /// <param name="mergePolicies">Set of auto-merge policies.</param>
        /// <returns>Newly created subscription.</returns>
        Task<Subscription> CreateSubscriptionAsync(
            string channelName,
            string sourceRepo,
            string targetRepo,
            string targetBranch,
            string updateFrequency,
            bool batchable,
            List<MergePolicy> mergePolicies);

        /// <summary>
        ///     Update an existing subscription
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to update</param>
        /// <param name="subscription">Subscription information</param>
        /// <returns>Updated subscription</returns>
        Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdate subscription);

        /// <summary>
        ///     Delete a subscription by ID.
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to delete.</param>
        /// <returns>Information on deleted subscription</returns>
        Task<Subscription> DeleteSubscriptionAsync(string subscriptionId);

        /// <summary>
        ///     Get repository merge policies
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Repository branch</param>
        /// <returns>List of merge policies</returns>
        Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(string repoUri, string branch);

        /// <summary>
        ///     Get a list of repository+branch combos and their associated merge policies.
        /// </summary>
        /// <param name="repoUri">Optional repository</param>
        /// <param name="branch">Optional branch</param>
        /// <returns>List of repository+branch combos</returns>
        Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string repoUri = null, string branch = null);

        /// <summary>
        ///     Set the merge policies for batchable subscriptions applied to a specific repo and branch
        /// </summary>
        /// <param name="repoUri">Repository</param>
        /// <param name="branch">Branch</param>
        /// <param name="mergePolicies">Merge policies. May be empty.</param>
        /// <returns>Task</returns>
        Task SetRepositoryMergePoliciesAsync(string repoUri, string branch, List<MergePolicy> mergePolicies);

        #endregion

        #region Pull Request Operations

        /// <summary>
        ///  Merges a pull request created by a dependency update
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request to merge</param>
        /// <param name="parameters">Merge options.</param>
        /// <returns>Async task.</returns>
        Task MergeDependencyPullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters);

        /// <summary>
        ///     Create new check(s), update them with a new status,
        ///     or remove each merge policy check that isn't in evaluations
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request.</param>
        /// <param name="evaluations">List of merge policies.</param>
        /// <returns>Async task.</returns>
        Task CreateOrUpdatePullRequestMergeStatusInfoAsync(string pullRequestUrl, IReadOnlyList<MergePolicyEvaluationResult> evaluations);

        /// <summary>
        ///     Get the status of a pull request.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request.</param>
        /// <returns>PR status information.</returns>
        Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl);

        /// <summary>
        ///     Get the checks that are being run on a pull request.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request.</param>
        /// <returns>Async task.</returns>
        Task<IEnumerable<Check>> GetPullRequestChecksAsync(string pullRequestUrl);

        /// <summary>
        ///     Get the reviews for the specified pull request.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request.</param>
        /// <returns>List of reviews</returns>
        Task<IEnumerable<Review>> GetPullRequestReviewsAsync(string pullRequestUrl);

        /// <summary>
        ///     Retrieve information about a pull request.
        /// </summary>
        /// <param name="pullRequestUri">URI of pull request.</param>
        /// <returns>Pull request information.</returns>
        Task<PullRequest> GetPullRequestAsync(string pullRequestUri);

        /// <summary>
        ///     Create a new pull request.
        /// </summary>
        /// <param name="repoUri">Repository uri.</param>
        /// <param name="pullRequest">Information about pull request to create.</param>
        /// <returns>URI of new pull request.</returns>
        Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest);

        /// <summary>
        ///     Update a pull request with new data.
        /// </summary>
        /// <param name="pullRequestUri">URI of pull request to update</param>
        /// <param name="pullRequest">Pull request information to update.</param>
        /// <returns>Async task.</returns>
        Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest);

        /// <summary>
        ///     Delete a Pull Request branch
        /// </summary>
        /// <param name="pullRequestUri">URI of pull request to delete branch for</param>
        /// <returns>Async task</returns>
        Task DeletePullRequestBranchAsync(string pullRequestUri);

        #endregion

        #region Repo/Dependency Operations

        /// <summary>
        ///     Get the list of dependencies in the specified repo and branch/commit
        /// </summary>
        /// <param name="repoUri">Repository to get dependencies from</param>
        /// <param name="branchOrCommit">Commit to get dependencies at</param>
        /// <param name="name">Optional name of specific dependency to get information on</param>
        /// <param name="loadLocations">Optional switch to populate dependency locations from BAR</param>
        /// <returns>Matching dependency information.</returns>
        Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri, string branchOrCommit, string name = null, bool loadLocations = false);

        /// <summary>
        ///     Get updates required by coherency constraints.
        /// </summary>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <param name="remoteFactory">Remote factory for remote queries.</param>
        /// <returns>List of dependency updates.</returns>
        Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            IEnumerable<DependencyDetail> dependencies,
            IRemoteFactory remoteFactory,
            CoherencyMode coherencyMode);

        /// <summary>
        ///     Given a current set of dependencies, determine what non-coherency updates
        ///     are required.
        /// </summary>
        /// <param name="sourceRepoUri">Repository that <paramref name="assets"/> came from.</param>
        /// <param name="sourceCommit">Commit that <paramref name="assets"/> came from.</param>
        /// <param name="assets">Assets to apply</param>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <returns>List of dependency updates.</returns>
        Task<List<DependencyUpdate>> GetRequiredNonCoherencyUpdatesAsync(
            string sourceRepoUri,
            string sourceCommit,
            IEnumerable<AssetData> assets,
            IEnumerable<DependencyDetail> dependencies);

        /// <summary>
        /// Retrieve the common script files from a remote source.
        /// </summary>
        /// <param name="repoUri">URI of repo containing script files.</param>
        /// <param name="commit">Common to get script files at.</param>
        /// <returns>Script files.</returns>
        Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit);

        /// <summary>
        /// Get the tools.dotnet section of the global.json from a target repo URI
        /// </summary>
        /// <param name="repoUri">repo to get the version from</param>
        /// <param name="commit">commit sha to query</param>
        /// <returns></returns>
        Task<SemanticVersion> GetToolsDotnetVersionAsync(string repoUri, string commit);

        /// <summary>
        ///     Create a new branch in the specified repository.
        /// </summary>
        /// <param name="repoUri">Repository to create a branch in</param>
        /// <param name="baseBranch">Branch to create <paramref name="newBranch"/> off of</param>
        /// <param name="newBranch">New branch name.</param>
        /// <returns>Async task</returns>
        Task CreateNewBranchAsync(string repoUri, string baseBranch, string newBranch);

        /// <summary>
        ///     Delete a branch in a repository.
        /// </summary>
        /// <param name="repoUri">Repository to delete a branch in.</param>
        /// <param name="branch">Branch to delete.</param>
        /// <returns>Async task.</returns>
        Task DeleteBranchAsync(string repoUri, string branch);

        /// <summary>
        ///     Commit a set of updated dependencies to a repository
        /// </summary>
        /// <param name="repoUri">Repository to update</param>
        /// <param name="branch">Branch of <paramref name="repoUri"/> to update.</param>
        /// <param name="remoteFactory">Remote factory for obtaining common script files from arcade</param>
        /// <param name="itemsToUpdate">Dependencies that need updating.</param>
        /// <param name="message">Commit message.</param>
        /// <returns>Async task.</returns>
        Task<List<GitFile>> CommitUpdatesAsync(string repoUri, string branch, IRemoteFactory remoteFactory, 
            List<DependencyDetail> itemsToUpdate, string message);

        /// <summary>
        ///     Diff two commits in a repository and return information about them.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="baseCommit">Base version</param>
        /// <param name="targetCommit">Target version</param>
        /// <returns>Diff information</returns>
        Task<GitDiff> GitDiffAsync(string repoUri, string baseCommit, string targetCommit);

        /// <summary>
        ///     Get the latest commit in a branch
        /// </summary>
        /// <param name="repoUri">Remote repository</param>
        /// <param name="branch">Branch</param>
        /// <returns>Latest commit</returns>
        Task<string> GetLatestCommitAsync(string repoUri, string branch);


        /// <summary>
        /// Checks that a repository exists
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <returns>True if the repository exists, false otherwise.</returns>
        Task<bool> RepositoryExistsAsync(string repoUri);

        /// <summary>
        ///     Clone a remote repo.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Branch, commit, or tag to checkout</param>
        /// <param name="targetDirectory">Directory to clone the repo to</param>
        /// <param name="gitDirParent">Location for the .git directory, or null for default</param>
        /// <returns></returns>
        void Clone(string repoUri, string commit, string targetDirectory, string gitDirectory);

        #endregion

        #region Build/Asset Operations

        /// <summary>
        ///     Retrieve the latest build of a repository on a specific channel.
        /// </summary>
        /// <param name="repoUri">URI of repository to obtain a build for.</param>
        /// <param name="channelId">Channel the build was applied to.</param>
        /// <returns>Latest build of <paramref name="repoUri"/> on channel <paramref name="channelId"/>,
        /// or null if there is no latest.</returns>
        /// <remarks>The build's assets are returned</remarks>
        Task<Build> GetLatestBuildAsync(string repoUri, int channelId);

        /// <summary>
        ///     Retrieve information about the specified build.
        /// </summary>
        /// <param name="buildId">Id of build.</param>
        /// <returns>Information about the specific build</returns>
        /// <remarks>The build's assets are returned</remarks>
        Task<Build> GetBuildAsync(int buildId);

        /// <summary>
        ///     Get a list of builds for the given repo uri and commit.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Commit</param>
        /// <returns></returns>
        Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit);

        /// <summary>
        ///     Assign a particular build to a channel
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <param name="channelId">Channel id</param>
        /// <returns>Async task</returns>
        Task AssignBuildToChannelAsync(int buildId, int channelId);

        /// <summary>
        ///     Remove a particular build from a channel
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <param name="channelId">Channel id</param>
        /// <returns>Async task</returns>
        Task DeleteBuildFromChannelAsync(int buildId, int channelId);

        /// <summary>
        ///     Get assets matching a particular set of properties. All are optional.
        /// </summary>
        /// <param name="name">Name of asset</param>
        /// <param name="version">Version of asset</param>
        /// <param name="buildId">ID of build producing the asset</param>
        /// <param name="nonShipping">Only non-shipping</param>
        /// <returns>List of assets.</returns>
        Task<IEnumerable<Asset>> GetAssetsAsync(string name = null, string version = null, int? buildId = null, bool? nonShipping = null);

        /// <summary>
        ///     Update a list of dependencies with asset locations.
        /// </summary>
        /// <param name="dependencies">Dependencies to load locations for</param>
        /// <returns>Async task</returns>
        Task AddAssetLocationToDependenciesAsync(IEnumerable<DependencyDetail> dependencies);

        /// <summary>
        ///     Update an existing build.
        /// </summary>
        /// <param name="buildId">Build to update</param>
        /// <param name="buildUpdate">Updated build info</param>
        Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate);

        #endregion

        #region Goal Operations

        /// <summary>
        /// Creates a new goal or updates the existing goal (in minutes) for a Defintion in a Channel.
        /// </summary>
        /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
        /// <param name="definitionId">Azure DevOps DefinitionId</param>
        /// <param name="minutes">Goal in minutes for a Definition in a Channel.</param>
        /// <returns>Task</returns>
        Task<Goal> SetGoalAsync(string channel, int definitionId, int minutes);

        /// <summary>
        ///     Gets goal (in minutes) for a Defintion in a Channel.
        /// </summary>
        /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
        /// <param name="definitionId">Azure DevOps DefinitionId.</param>
        /// <returns>Returns Goal in minutes.</returns>
        Task<Goal> GetGoalAsync(string channel, int definitionId);

        /// <summary>
        ///     Gets official and pr build times (in minutes) for a default channel summarized over a number of days.
        /// </summary>
        /// <param name="defaultChannelId">Id of the default channel</param>
        /// <param name="days">Number of days to summarize over</param>
        /// <returns>Returns BuildTime in minutes</returns>
        Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days);
        #endregion
    }
}
