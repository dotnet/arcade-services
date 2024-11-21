// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// A simplified set of BAR operations implemented using both API and direct database access.
/// Service Fabric services use the implementation with the direct access while darc uses the API.
/// </summary>
public interface IBasicBarClient
{
    #region Subscription Operations

    /// <summary>
    ///     Retrieve a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Subscription information</returns>
    Task<Subscription> GetSubscriptionAsync(Guid subscriptionId);

    /// <summary>
    ///     Retrieve a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Subscription information</returns>
    Task<Subscription> GetSubscriptionAsync(string subscriptionId);

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

    #endregion

    #region Channel Operations

    /// <summary>
    ///     Retrieve a specific channel by id.
    /// </summary>
    /// <param name="channel">Channel id.</param>
    /// <returns>Channel or null if not found.</returns>
    Task<Channel> GetChannelAsync(int channelId);

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

    Task<DependencyFlowGraph> GetDependencyFlowGraphAsync(
        int channelId,
        int days,
        bool includeArcade,
        bool includeBuildTimes,
        bool includeDisabledSubscriptions,
        IReadOnlyList<string> includedFrequencies = default);

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

    #endregion

    #region Goal Operations

    /// <summary>
    ///     Gets official and pr build time (in minutes) for a default channel summarized over a number of days.
    /// </summary>
    /// <param name="defaultChannelId">Id of the default channel</param>
    /// <param name="days">Number of days to summarize over</param>
    /// <returns>Returns BuildTime in minutes.</returns>
    Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days);

    #endregion
}
