﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client.Models;
using Asset = Microsoft.DotNet.Maestro.Client.Models.Asset;
using Subscription = Microsoft.DotNet.Maestro.Client.Models.Subscription;

namespace Microsoft.DotNet.DarcLib;

public interface IBarRemote
{
    #region Repo/Dependency Operations

    /// <summary>
    ///   Gets dependency flow graph for given channel.
    /// </summary>
    /// <param name="channelId">Channel ID</param>
    /// <param name="days">Number of days over which the build times will be summarized</param>
    /// <param name="includeArcade">Should arcade be included in generated graph</param>
    /// <param name="includeBuildTimes">Should build times be calculated for each node</param>
    /// <param name="includeDisabledSubscriptions">Should disabled subscriptions be included in the graph</param>
    /// <param name="includedFrequencies">Include only subscription with specified frequencies. Leave null or empty to include all</param>
    /// <returns>Dependency flow graph for given channel</returns>
    Task<DependencyFlowGraph> GetDependencyFlowGraphAsync(
        int channelId,
        int days,
        bool includeArcade,
        bool includeBuildTimes,
        bool includeDisabledSubscriptions,
        IReadOnlyList<string> includedFrequencies = default);

    #endregion

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
        List<MergePolicy> mergePolicies,
        string failureNotificationTags);

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

    #region Repo/Dependency Operations

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
    Task<IEnumerable<Asset>> GetAssetsAsync(
        string name = null,
        string version = null,
        int? buildId = null,
        bool? nonShipping = null);

    /// <summary>
    ///     Update a list of dependencies with asset locations.
    /// </summary>
    /// <param name="dependencies">Dependencies to load locations for</param>
    /// <returns>Async task</returns>
    Task AddAssetLocationToDependenciesAsync(IReadOnlyCollection<DependencyDetail> dependencies);

    /// <summary>
    ///     Update an existing build.
    /// </summary>
    /// <param name="buildId">Build to update</param>
    /// <param name="buildUpdate">Updated build info</param>
    Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate);

    #endregion

    #region Goal Operations

    /// <summary>
    ///     Gets official and pr build times (in minutes) for a default channel summarized over a number of days.
    /// </summary>
    /// <param name="defaultChannelId">Id of the default channel</param>
    /// <param name="days">Number of days to summarize over</param>
    /// <returns>Returns BuildTime in minutes</returns>
    Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days);

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

    #endregion
}
