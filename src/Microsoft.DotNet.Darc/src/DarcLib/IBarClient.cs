// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    /// <summary>
    ///     Represents basic operations that can be done against BAR, for use in
    ///     Remote
    /// </summary>
    public interface IBarClient
    {
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
        Task<Subscription> UpdateSubscriptionAsync(Guid subscriptionId, SubscriptionUpdate subscription);

        /// <summary>
        ///     Retrieve a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">Id of subscription</param>
        /// <returns>Subscription information</returns>
        Task<Subscription> GetSubscriptionAsync(Guid subscriptionId);

        /// <summary>
        ///     Get a repository merge policy (for batchable subscriptions)
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="branch">Repository branch</param>
        /// <returns>List of merge policies</returns>
        Task<IEnumerable<MergePolicy>> GetRepositoryMergePolicies(string repoUri, string branch);

        /// <summary>
        /// Trigger a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <returns>Subscription just triggered.</returns>
        Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId);

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
        /// <returns>List of builds</returns>
        Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit);

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
        ///     Assign a particular build to a channel.
        /// </summary>
        /// <param name="buildId">Build id</param>
        /// <param name="channelId">Channel id</param>
        /// <returns>Async task</returns>
        Task AssignBuildToChannel(int buildId, int channelId);

        #endregion
    }
}
