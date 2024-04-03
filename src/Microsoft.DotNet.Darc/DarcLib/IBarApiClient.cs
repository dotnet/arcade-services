// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
///     Represents basic operations that can be done against BAR, for use in
///     Remote
/// </summary>
public interface IBarApiClient : IBasicBarClient
{
    #region Subscription Operations

    /// <summary>
    ///     Create a new subscription.
    /// </summary>
    /// <param name="channelName">Name of source channel.</param>
    /// <param name="sourceRepo">Source repository URI.</param>
    /// <param name="targetRepo">Target repository URI.</param>
    /// <param name="targetBranch">Target branch in <paramref name="targetRepo"/></param>
    /// <param name="updateFrequency">Frequency of update.  'none', 'everyBuild', 'everyDay', 'twiceDaily', or 'everyWeek'.</param>
    /// <param name="batchable">Is subscription batchable.</param>
    /// <param name="mergePolicies">Set of auto-merge policies.</param>
    /// <param name="failureNotificationTags">List of GitHub tags to notify with a PR comment when the build fails</param>
    /// <param name="sourceEnabled">Whether this is a VMR code flow (special VMR subscription)</param>
    /// <param name="sourceDirectory">Directory of the VMR to synchronize the sources from</param>
    /// <param name="targetDirectory">Directory of the VMR to synchronize the sources to</param>
    /// <param name="excludedAssets">List of assets to exclude from the source-enabled code flow</param>
    /// <returns>Newly created subscription.</returns>
    Task<Subscription> CreateSubscriptionAsync(
        string channelName,
        string sourceRepo,
        string targetRepo,
        string targetBranch,
        string updateFrequency,
        bool batchable,
        List<MergePolicy> mergePolicies,
        string failureNotificationTags,
        bool sourceEnabled,
        string sourceDirectory,
        string targetDirectory,
        IReadOnlyCollection<string> excludedAssets);

    /// <summary>
    ///     Update an existing subscription
    /// </summary>
    /// <param name="subscriptionId">Id of subscription to update</param>
    /// <param name="subscription">Subscription information</param>
    /// <returns>Updated subscription</returns>
    Task<Subscription> UpdateSubscriptionAsync(Guid subscriptionId, SubscriptionUpdate subscription);

    /// <summary>
    ///     Update an existing subscription
    /// </summary>
    /// <param name="subscriptionId">Id of subscription to update</param>
    /// <param name="subscription">Subscription information</param>
    /// <returns>Updated subscription</returns>
    Task<Subscription> UpdateSubscriptionAsync(string subscriptionId, SubscriptionUpdate subscription);

    /// <summary>
    ///     Get a repository merge policy (for batchable subscriptions)
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
    Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string repoUri, string branch);

    /// <summary>
    ///     Set the merge policies for batchable subscriptions applied to a specific repo and branch
    /// </summary>
    /// <param name="repoUri">Repository</param>
    /// <param name="branch">Branch</param>
    /// <param name="mergePolicies">Merge policies. May be empty.</param>
    /// <returns>Task</returns>
    Task SetRepositoryMergePoliciesAsync(string repoUri, string branch, List<MergePolicy> mergePolicies);

    /// <summary>
    /// Trigger a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">ID of subscription to trigger</param>
    /// <returns>Subscription just triggered.</returns>
    Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId);

    /// <summary>
    /// Trigger a subscription by ID and source build id.
    /// </summary>
    /// <param name="subscriptionId">ID of subscription to trigger</param>
    /// <returns>Subscription just triggered.</returns>
    Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, int sourceBuildId);

    /// <summary>
    ///     Delete a subscription by ID.
    /// </summary>
    /// <param name="subscriptionId">Id of subscription to delete.</param>
    /// <returns>Information on deleted subscription</returns>
    Task<Subscription> DeleteSubscriptionAsync(Guid subscriptionId);

    #endregion

    #region Channel Operations

    /// <summary>
    ///     Retrieve a specific channel by name.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <returns>Channel or null if not found.</returns>
    Task<Channel> GetChannelAsync(string channel);

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

    #endregion

    #region Build/Asset Operations

    /// <summary>
    ///     Assign a particular build to a channel.
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
    ///     Update an existing build.
    /// </summary>
    /// <param name="buildId">Build to update</param>
    /// <param name="buildUpdate">Updated build info</param>
    Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate);

    #endregion

    #region Goal Operations

    /// <summary>
    ///     Creates a new goal or updates the existing goal (in minutes) for a Defintion in a Channel.
    /// </summary>
    /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
    /// <param name="definitionId">Azure DevOps DefinitionId.</param>
    /// <param name="minutes">Goal in minutes for a Definition in a Channel.</param>
    /// <returns>Async task.</returns>
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
