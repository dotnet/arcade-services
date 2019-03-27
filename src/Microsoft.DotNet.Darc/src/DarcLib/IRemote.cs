// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        ///     Removes a default channel based on the specified criteria
        /// </summary>
        /// <param name="repository">Repository having a default association</param>
        /// <param name="branch">Branch having a default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' are being applied to.</param>
        /// <returns>Async task</returns>
        Task DeleteDefaultChannelAsync(string repository, string branch, string channel);

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

        #endregion

        #region Subscription Operations

        /// <summary>
        ///     Get a set of subscriptions based on input filters.
        /// </summary>
        /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
        /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
        /// <param name="channelId">Filter by the target channel id of the subscription.</param>
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
        ///     Create a new subscription.
        /// </summary>
        /// <param name="channelName">Name of source channel.</param>
        /// <param name="sourceRepo">Source repository URI.</param>
        /// <param name="targetRepo">Target repository URI.</param>
        /// <param name="targetBranch">Target branch in <paramref name="targetRepo"/></param>
        /// <param name="updateFrequency">Frequency of update.  'none', 'everyDay', or 'everyBuild'.</param>
        /// <param name="mergePolicies">Set of auto-merge policies.</param>
        /// <returns>Newly created subscription.</returns>
        Task<Subscription> CreateSubscriptionAsync(
            string channelName,
            string sourceRepo,
            string targetRepo,
            string targetBranch,
            string updateFrequency,
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

        #endregion

        #region Pull Request Operations
        /// <summary>
        ///     Merge a pull request.
        /// </summary>
        /// <param name="pullRequestUrl">Uri of pull request to merge</param>
        /// <param name="parameters">Merge options.</param>
        /// <returns>Async task.</returns>
        Task MergePullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters);

        /// <summary>
        ///     Create a comment on a pull request, or update the last comment if it was made by Maestro.
        /// </summary>
        /// <param name="pullRequestUrl">Url of pull request.</param>
        /// <param name="message">Comment message.</param>
        /// <returns>Async task.</returns>
        Task CreateOrUpdatePullRequestStatusCommentAsync(string pullRequestUrl, string message);

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

        #endregion

        #region Repo/Dependency Operations

        /// <summary>
        ///     Get the list of dependencies in the specified repo and branch/commit
        /// </summary>
        /// <param name="repoUri">Repository to get dependencies from</param>
        /// <param name="branchOrCommit">Commit to get dependencies at</param>
        /// <param name="name">Optional name of specific dependency to get information on</param>
        /// <returns>Matching dependency information.</returns>
        Task<IEnumerable<DependencyDetail>> GetDependenciesAsync(string repoUri, string branchOrCommit, string name = null);

        /// <summary>
        ///     Given a repo and branch, determine what updates are required to satisfy
        ///     coherency constraints.
        /// </summary>
        /// <param name="repoUri">Repository uri to check for updates in.</param>
        /// <param name="branch">Branch to check for updates in.</param>
        /// <param name="remoteFactory">Remote factory use in walking the repo dependency graph.</param>
        /// <returns>List of dependency updates.</returns>
        Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            string repoUri,
            string branch,
            IRemoteFactory remoteFactory);

        /// <summary>
        ///     Get updates required by coherency constraints.
        /// </summary>
        /// <param name="dependencies">Current set of dependencies.</param>
        /// <param name="remoteFactory">Remote factory for remote queries.</param>
        /// <returns>List of dependency updates.</returns>
        Task<List<DependencyUpdate>> GetRequiredCoherencyUpdatesAsync(
            IEnumerable<DependencyDetail> dependencies,
            IRemoteFactory remoteFactory);

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
        ///     Get the list of dependencies that need updates given an input set of asset data.
        /// </summary>
        /// <param name="repoUri">Repository that may need updates.</param>
        /// <param name="branch">Branch in <paramref name="repoUri"/> that may need updates.</param>
        /// <param name="sourceCommit">Source commit the assets came from.</param>
        /// <param name="assets">Set of updated assets.</param>
        /// <returns>List of dependency updates.</returns>
        Task<List<DependencyUpdate>> GetRequiredNonCoherencyUpdatesAsync(
            string repoUri,
            string branch,
            string sourceRepoUri,
            string sourceCommit,
            IEnumerable<AssetData> assets);

        /// <summary>
        /// Retrieve the common script files from a remote source.
        /// </summary>
        /// <param name="repoUri">URI of repo containing script files.</param>
        /// <param name="commit">Common to get script files at.</param>
        /// <returns>Script files.</returns>
        Task<List<GitFile>> GetCommonScriptFilesAsync(string repoUri, string commit);

        /// <summary>
        ///     Create a new branch in the specified repository.
        /// </summary>
        /// <param name="repoUri">Repository to create a brahc in</param>
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
        /// <param name="itemsToUpdate">Dependencies that need updating.</param>
        /// <param name="message">Commit message.</param>
        /// <returns>Async task.</returns>
        Task CommitUpdatesAsync(string repoUri, string branch, List<DependencyDetail> itemsToUpdate, string message);

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
        ///     Clone a remote repo.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Branch, commit, or tag to checkout</param>
        /// <param name="targetDirectory">Directory to clone the repo to</param>
        /// <returns></returns>
        Task CloneAsync(string repoUri, string commit, string targetDirectory);

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
        Task AssignBuildToChannel(int buildId, int channelId);

        /// <summary>
        ///     Get assets matching a particular set of properties. All are optional.
        /// </summary>
        /// <param name="name">Name of asset</param>
        /// <param name="version">Version of asset</param>
        /// <param name="buildId">ID of build producing the asset</param>
        /// <param name="nonShipping">Only non-shipping</param>
        /// <returns>List of assets.</returns>
        Task<IEnumerable<Asset>> GetAssetsAsync(string name = null, string version = null, int? buildId = null, bool? nonShipping = null);

        #endregion
    }
}
