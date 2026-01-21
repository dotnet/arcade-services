// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using AsyncEnumerable = Microsoft.DotNet.ProductConstructionService.Client.AsyncEnumerable;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class BarApiClient : IBarApiClient
{
    private readonly IProductConstructionServiceApi _barClient;

    public BarApiClient(string? buildAssetRegistryPat, string? managedIdentityId, bool disableInteractiveAuth, string? buildAssetRegistryBaseUri = null)
    {
        _barClient = !string.IsNullOrEmpty(buildAssetRegistryBaseUri)
            ? PcsApiFactory.GetAuthenticated(buildAssetRegistryBaseUri, buildAssetRegistryPat, managedIdentityId, disableInteractiveAuth)
            : PcsApiFactory.GetAuthenticated(buildAssetRegistryPat, managedIdentityId, disableInteractiveAuth);
    }

    #region Channel Operations

    /// <summary>
    ///     Retrieve a list of default channel associations.
    /// </summary>
    /// <param name="repository">Optionally filter by repository</param>
    /// <param name="branch">Optionally filter by branch</param>
    /// <param name="channel">Optionally filter by channel</param>
    /// <returns>Collection of default channels.</returns>
    public async Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(string? repository = null, string? branch = null, string? channel = null)
    {
        IReadOnlyList<DefaultChannel> channels = await _barClient.DefaultChannels.ListAsync(repository: repository, branch: branch);
        if (!string.IsNullOrEmpty(channel))
        {
            return channels.Where(c => c.Channel.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
        }

        // Filter away based on channel info.
        return channels;
    }

    /// <summary>
    ///     Find a single channel by name.
    /// </summary>
    /// <param name="channel">Channel to find.</param>
    /// <returns>Channel object or throws</returns>
    private async Task<Channel> GetChannel(string channel)
        => (await _barClient.Channels.ListChannelsAsync())
            .SingleOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Channel {channel} is not a valid channel.");

    public async Task<DependencyFlowGraph> GetDependencyFlowGraphAsync(
        int channelId,
        int days,
        bool includeArcade,
        bool includeBuildTimes,
        bool includeDisabledSubscriptions,
        IReadOnlyList<string> includedFrequencies)
    {
        var flowGraph = await _barClient.Channels.GetFlowGraphAsync(
            channelId: channelId,
            days: days,
            includeArcade: includeArcade,
            includeBuildTimes: includeBuildTimes,
            includeDisabledSubscriptions: includeDisabledSubscriptions,
            includedFrequencies: [..includedFrequencies ?? []]);

        var subscriptions = await _barClient.Subscriptions.ListSubscriptionsAsync();
        var subscriptionsById = subscriptions.ToDictionary(s => s.Id);

        var nodes = flowGraph.FlowRefs.Select(ToDependencyFlowNode).ToList();
        var nodesById = nodes.ToImmutableDictionary(n => n.Id);

        var edges = flowGraph.FlowEdges
            .Select(edge => ToDependencyFlowEdge(edge, nodesById, subscriptionsById))
            .ToList();

        foreach (var edge in edges)
        {
            edge.From.OutgoingEdges.Add(edge);
            edge.To.IncomingEdges.Add(edge);
        }

        return new DependencyFlowGraph(nodes, edges);
    }

    private static DependencyFlowNode ToDependencyFlowNode(FlowRef flowRef)
    {
        return new DependencyFlowNode(flowRef.Repository, flowRef.Branch, flowRef.Id)
        {
            BestCasePathTime = flowRef.BestCasePathTime,
            GoalTimeInMinutes = flowRef.GoalTimeInMinutes,
            InputChannels = [.. flowRef.InputChannels],
            OfficialBuildTime = flowRef.OfficialBuildTime,
            OnLongestBuildPath = flowRef.OnLongestBuildPath,
            OutputChannels = [.. flowRef.OutputChannels],
            PrBuildTime = flowRef.PrBuildTime,
            WorstCasePathTime = flowRef.WorstCasePathTime
        };
    }

    private static DependencyFlowEdge ToDependencyFlowEdge(
        FlowEdge flowEdge,
        ImmutableDictionary<string, DependencyFlowNode> nodesById,
        Dictionary<Guid, Subscription> subscriptionsById)
    {
        var fromNode = nodesById[flowEdge.FromId];
        var toNode = nodesById[flowEdge.ToId];
        var subscription = subscriptionsById[flowEdge.SubscriptionId];
        return new DependencyFlowEdge(fromNode, toNode, subscription)
        {
            BackEdge = flowEdge.BackEdge,
            IsToolingOnly = flowEdge.IsToolingOnly,
            OnLongestBuildPath = flowEdge.OnLongestBuildPath,
            PartOfCycle = flowEdge.PartOfCycle
        };
    }

    #endregion

    #region Subscription Operations

    /// <summary>
    ///     Get a set of subscriptions based on input filters.
    /// </summary>
    /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
    /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
    /// <param name="channelId">Filter by the source channel id of the subscription.</param>
    /// <param name="sourceEnabled">Filter by source-enabled subscriptions.</param>
    /// <param name="sourceDirectory">Filter by source directory.</param>
    /// <param name="targetDirectory">Filter by target directory.</param>
    /// <returns>Set of subscription.</returns>
    public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
        string? sourceRepo = null,
        string? targetRepo = null,
        int? channelId = null,
        bool? sourceEnabled = null,
        string? sourceDirectory = null,
        string? targetDirectory = null)
    {
        return await _barClient.Subscriptions.ListSubscriptionsAsync(
            sourceRepository: sourceRepo,
            targetRepository: targetRepo,
            channelId: channelId,
            sourceEnabled: sourceEnabled,
            sourceDirectory: sourceDirectory,
            targetDirectory: targetDirectory);
    }

    /// <summary>
    /// Trigger a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">ID of subscription to trigger</param>
    /// <param name="force">Force update even for PRs with pending or successful checks</param>
    /// <returns>Subscription just triggered.</returns>
    public Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, bool force = false)
    {
        return _barClient.Subscriptions.TriggerSubscriptionAsync(subscriptionId, force);
    }

    public Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, int sourceBuildId, bool force = false)
    {
        return _barClient.Subscriptions.TriggerSubscriptionAsync(sourceBuildId, force, subscriptionId);
    }

    /// <summary>
    ///     Retrieve a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Subscription information</returns>
    public Task<Subscription> GetSubscriptionAsync(Guid subscriptionId)
    {
        return _barClient.Subscriptions.GetSubscriptionAsync(subscriptionId);
    }

    /// <summary>
    ///     Retrieve a subscription by ID
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Subscription information</returns>
    public Task<Subscription> GetSubscriptionAsync(string subscriptionId)
    {
        return GetSubscriptionAsync(Guid.Parse(subscriptionId));
    }

    /// <summary>
    ///     Get a repository merge policy (for batchable subscriptions)
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="branch">Repository branch</param>
    /// <returns>List of merge policies</returns>
    public async Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(string repoUri, string branch)
    {
        try
        {
            return await _barClient.Repository.GetMergePoliciesAsync(repository: repoUri, branch: branch);
        }
        catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
        {
            // Return an empty list
            return new List<MergePolicy>();
        }
    }

    /// <summary>
    ///     Get a list of repository+branch combos and their associated merge policies.
    /// </summary>
    /// <param name="repoUri">Optional repository</param>
    /// <param name="branch">Optional branch</param>
    /// <returns>List of repository+branch combos</returns>
    public async Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string? repoUri = null, string? branch = null)
    {
        return await _barClient.Repository.ListRepositoriesAsync(repository: repoUri, branch: branch);
    }


    /// <summary>
    ///     Set the merge policies for batchable subscriptions applied to a specific repo and branch
    /// </summary>
    /// <param name="repoUri">Repository</param>
    /// <param name="branch">Branch</param>
    /// <param name="mergePolicies">Merge policies. May be empty.</param>
    /// <returns>Task</returns>
    public async Task SetRepositoryMergePoliciesAsync(string repoUri, string branch, List<MergePolicy> mergePolicies)
    {
        await _barClient.Repository.SetMergePoliciesAsync(repository: repoUri, branch: branch, body: mergePolicies);
    }

    #endregion

    #region Pull Request Operations

    /// <summary>
    ///     Get a tracked pull request by subscription ID.
    /// </summary>
    /// <param name="subscriptionId">Id of subscription</param>
    /// <returns>Tracked pull request information</returns>
    public async Task<TrackedPullRequest> GetTrackedPullRequestBySubscriptionIdAsync(Guid subscriptionId)
    {
        return await _barClient.PullRequest.GetTrackedPullRequestBySubscriptionIdAsync(subscriptionId.ToString());
    }

    #endregion

    #region Build/Asset Operations

    /// <summary>
    ///     Get assets matching a particular set of properties. All are optional.
    /// </summary>
    /// <param name="name">Name of asset</param>
    /// <param name="version">Version of asset</param>
    /// <param name="buildId">ID of build producing the asset</param>
    /// <param name="nonShipping">Only non-shipping</param>
    /// <returns>List of assets.</returns>
    public async Task<IEnumerable<Asset>> GetAssetsAsync(
        string? name = null,
        string? version = null,
        int? buildId = null,
        bool? nonShipping = null)
    {
        AsyncPageable<Asset> pagedResponse = _barClient.Assets.ListAssetsAsync(name: name,
            version: version, buildId: buildId, loadLocations: true);
        return await AsyncEnumerable.ToListAsync(pagedResponse, CancellationToken.None);
    }

    /// <summary>
    ///     Retrieve information about the specified build.
    /// </summary>
    /// <param name="buildId">Id of build.</param>
    /// <returns>Information about the specific build</returns>
    /// <remarks>The build's assets are returned</remarks>
    public Task<Build> GetBuildAsync(int buildId)
    {
        return _barClient.Builds.GetBuildAsync(buildId);
    }

    /// <summary>
    ///     Get a list of builds for the given repo uri and commit.
    /// </summary>
    /// <param name="repoUri">Repository uri</param>
    /// <param name="commit">Commit</param>
    /// <returns></returns>
    public async Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
    {
        AsyncPageable<Build> pagedResponse = _barClient.Builds.ListBuildsAsync(repository: repoUri,
            commit: commit, loadCollections: true);
        return await AsyncEnumerable.ToListAsync(pagedResponse, CancellationToken.None);
    }

    public async Task AssignBuildToChannelAsync(int buildId, int channelId)
    {
        await _barClient.Channels.AddBuildToChannelAsync(buildId, channelId);
    }

    public async Task DeleteBuildFromChannelAsync(int buildId, int channelId)
    {
        await _barClient.Channels.RemoveBuildFromChannelAsync(buildId, channelId);
    }

    #endregion

    /// <summary>
    ///     Retrieve a specific channel by name.
    /// </summary>
    /// <param name="channel">Channel name.</param>
    /// <returns>Channel or null if not found.</returns>
    public async Task<Channel?> GetChannelAsync(string channel)
    {
        return (await _barClient.Channels.ListChannelsAsync())
            .FirstOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Retrieve a specific channel by id.
    /// </summary>
    /// <param name="channel">Channel id.</param>
    /// <returns>Channel or null if not found.</returns>
    public async Task<Channel?> GetChannelAsync(int channel)
    {
        try
        {
            return await _barClient.Channels.GetChannelAsync(channel);
        }
        catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    ///     Retrieve the list of channels from the build asset registry.
    /// </summary>
    /// <param name="classification">Optional classification to get</param>
    /// <returns></returns>
    public async Task<IEnumerable<Channel>> GetChannelsAsync(string? classification = null)
    {
        return await _barClient.Channels.ListChannelsAsync(classification);
    }

    /// <summary>
    ///     Retrieve the latest build of a repository on a specific channel.
    /// </summary>
    /// <param name="repoUri">URI of repository to obtain a build for.</param>
    /// <param name="channelId">Channel the build was applied to.</param>
    /// <returns>Latest build of <paramref name="repoUri"/> on channel <paramref name="channelId"/>,
    /// or null if there is no latest.</returns>
    /// <remarks>The build's assets are returned</remarks>
    public async Task<Build?> GetLatestBuildAsync(string repoUri, int channelId)
    {
        try
        {
            return await _barClient.Builds.GetLatestAsync(
                repository: repoUri,
                channelId: channelId,
                loadCollections: true);
        }
        catch (RestApiException<ApiError> e) when (e.Message.Contains("404 Not Found"))
        {
            return null;
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
        return _barClient.Builds.UpdateAsync(buildUpdate, buildId);
    }

    /// <summary>
    ///     Creates a new goal or updates the existing goal (in minutes) for a Defintion in a Channel.
    /// </summary>
    /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
    /// <param name="definitionId">Azure DevOps DefinitionId.</param>
    /// <param name="minutes">Goal in minutes for a Definition in a Channel.</param>
    /// <returns>Async task.</returns>
    public Task<Goal> SetGoalAsync(string channel, int definitionId, int minutes)
    {
        var jsonData = new GoalRequestJson(minutes: minutes);
        return _barClient.Goal.CreateAsync(body: jsonData, channelName : channel, definitionId : definitionId);
    }

    /// <summary>
    ///     Gets goal (in minutes) for a Defintion in a Channel.
    /// </summary>
    /// <param name="channel">Name of channel. For eg: .Net Core 5 Dev</param>
    /// <param name="definitionId">Azure DevOps DefinitionId.</param>
    /// <returns>Goal in minutes</returns>
    public Task<Goal> GetGoalAsync(string channel, int definitionId)
    {
        return _barClient.Goal.GetGoalTimesAsync(channelName: channel, definitionId: definitionId);
    }

    /// <summary>
    ///     Gets official and pr build time (in minutes) for a default channel summarized over a number of days.
    /// </summary>
    /// <param name="defaultChannelId">Id of the default channel</param>
    /// <param name="days">Number of days to summarize over</param>
    /// <returns>Returns BuildTime in minutes.</returns>
    public Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days)
    {
        return _barClient.BuildTime.GetBuildTimesAsync(id: defaultChannelId, days: days);
    }
}
