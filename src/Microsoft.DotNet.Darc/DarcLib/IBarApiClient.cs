// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
///     Represents basic operations that can be done against BAR, for use in
///     Remote
/// </summary>
public interface IBarApiClient : IBasicBarClient
{
    #region Subscription Operations

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
    /// <param name="force">Force update even for PRs with pending or successful checks</param>
    /// <returns>Subscription just triggered.</returns>
    Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, bool force = false);

    /// <summary>
    /// Trigger a subscription by ID and source build id.
    /// </summary>
    /// <param name="subscriptionId">ID of subscription to trigger</param>
    /// <param name="sourceBuildId">Source build ID</param>
    /// <param name="force">Force update even for PRs with pending or successful checks</param>
    /// <returns>Subscription just triggered.</returns>
    Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, int sourceBuildId, bool force = false);

    #endregion

    #region Pull Request Operations

    /// <summary>
    ///   Get a tracked pull request by subscription ID.
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Tracked pull request information</returns>
    Task<TrackedPullRequest> GetTrackedPullRequestBySubscriptionIdAsync(Guid subscriptionId);

    #endregion

    #region Channel Operations

    /// <summary>
    ///     Retrieve a specific channel by name.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <returns>Channel or null if not found.</returns>
    Task<Channel?> GetChannelAsync(string channel);

    /// <summary>
    ///     Retrieve the list of channels from the build asset registry.
    /// </summary>
    /// <param name="classification">Optional classification to get</param>
    /// <returns></returns>
    Task<IEnumerable<Channel>> GetChannelsAsync(string? classification = null);

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
