// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.DarcLib
{
    public class MaestroApiBarClient : IBarClient
    {
        IMaestroApi _barClient;

        public MaestroApiBarClient(string buildAssetRegistryPat, string buildAssetRegistryBaseUri = null)
        {
            if (!string.IsNullOrEmpty(buildAssetRegistryBaseUri))
            {
                _barClient = ApiFactory.GetAuthenticated(
                    buildAssetRegistryBaseUri,
                    buildAssetRegistryPat);
            }
            else
            {
                _barClient = ApiFactory.GetAuthenticated(buildAssetRegistryPat);
            }
        }

        #region Channel Operations

        /// <summary>
        ///     Retrieve a list of default channel associations.
        /// </summary>
        /// <param name="repository">Optionally filter by repository</param>
        /// <param name="branch">Optionally filter by branch</param>
        /// <param name="channel">Optionally filter by channel</param>
        /// <returns>Collection of default channels.</returns>
        public async Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(string repository = null, string branch = null, string channel = null)
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
        {
            Channel foundChannel = (await _barClient.Channels.ListChannelsAsync())
                .Where(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase))
                .SingleOrDefault();
            if (foundChannel == null)
            {
                throw new ArgumentException($"Channel {channel} is not a valid channel.");
            }
            return foundChannel;
        }

        /// <summary>
        ///     Adds a default channel association.
        /// </summary>
        /// <param name="repository">Repository receiving the default association</param>
        /// <param name="branch">Branch receiving the default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' should automatically be applied to.</param>
        /// <returns>Async task.</returns>
        public async Task AddDefaultChannelAsync(string repository, string branch, string channel)
        {
            // Look up channel to translate to channel id.
            Channel foundChannel = await GetChannel(channel);

            var defaultChannelsData = new DefaultChannelCreateData(repository: repository, branch: branch, channelId: foundChannel.Id);

            await _barClient.DefaultChannels.CreateAsync(defaultChannelsData);
        }

        /// <summary>
        ///     Removes a default channel based on the specified criteria
        /// </summary>
        /// <param name="repository">Repository having a default association</param>
        /// <param name="branch">Branch having a default association</param>
        /// <param name="channel">Name of channel that builds of 'repository' on 'branch' are being applied to.</param>
        /// <returns>Async task</returns>
        public async Task DeleteDefaultChannelAsync(int id)
        {
            await _barClient.DefaultChannels.DeleteAsync(id);
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
        public async Task UpdateDefaultChannelAsync(int id, string repository = null, string branch = null,
            string channel = null, bool? enabled = null)
        {
            Channel foundChannel = null;
            if (!string.IsNullOrEmpty(channel))
            {
                foundChannel = await GetChannel(channel);
            }

            DefaultChannelUpdateData updateData = new DefaultChannelUpdateData
            {
                Branch = branch,
                ChannelId = foundChannel?.Id,
                Enabled = enabled,
                Repository = repository
            };

            await _barClient.DefaultChannels.UpdateAsync(id, updateData);
        }

        /// <summary>
        ///     Create a new channel
        /// </summary>
        /// <param name="name">Name of channel. Must be unique.</param>
        /// <param name="classification">Classification of channel.</param>
        /// <returns>Newly created channel</returns>
        public Task<Channel> CreateChannelAsync(string name, string classification)
        {
            return _barClient.Channels.CreateChannelAsync(name: name, classification: classification);
        }

        /// <summary>
        /// Deletes a channel from the Build Asset Registry
        /// </summary>
        /// <param name="name">Name of channel</param>
        /// <returns>Channel just deleted</returns>
        public Task<Channel> DeleteChannelAsync(int id)
        {
            return _barClient.Channels.DeleteChannelAsync(id);
        }

        #endregion

        #region Subscription Operations

        /// <summary>
        ///     Create a new subscription
        /// </summary>
        /// <param name="channelName">Name of source channel</param>
        /// <param name="sourceRepo">URL of source repository</param>
        /// <param name="targetRepo">URL of target repository where updates should be made</param>
        /// <param name="targetBranch">Name of target branch where updates should be made</param>
        /// <param name="updateFrequency">Frequency of updates, can be 'none', 'everyBuild', 'everyDay', 'twiceDaily', or 'everyWeek'</param>
        /// <param name="mergePolicies">
        ///     Dictionary of merge policies. Each merge policy is a name of a policy with an associated blob
        ///     of metadata
        /// </param>
        /// <returns>Newly created subscription, if successful</returns>
        public Task<Subscription> CreateSubscriptionAsync(string channelName, string sourceRepo, string targetRepo,
            string targetBranch, string updateFrequency, bool batchable, List<MergePolicy> mergePolicies)
        {
            var subscriptionData = new SubscriptionData(
                channelName: channelName,
                sourceRepository: sourceRepo,
                targetRepository: targetRepo,
                targetBranch: targetBranch,
                policy: new SubscriptionPolicy(
                    batchable,
                    (UpdateFrequency) Enum.Parse(
                        typeof(UpdateFrequency),
                        updateFrequency,
                        ignoreCase: true))
                {
                    MergePolicies = mergePolicies.ToImmutableList(),
                });
            return _barClient.Subscriptions.CreateAsync(subscriptionData);
        }

        /// <summary>
        ///     Update an existing subscription
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to update</param>
        /// <param name="subscription">Subscription information</param>
        /// <returns>Updated subscription</returns>
        public Task<Subscription> UpdateSubscriptionAsync(Guid subscriptionId, SubscriptionUpdate subscription)
        {
            return _barClient.Subscriptions.UpdateSubscriptionAsync(subscriptionId, subscription);
        }

        /// <summary>
        ///     Get a set of subscriptions based on input filters.
        /// </summary>
        /// <param name="sourceRepo">Filter by the source repository of the subscription.</param>
        /// <param name="targetRepo">Filter by the target repository of the subscription.</param>
        /// <param name="channelId">Filter by the target channel id of the subscription.</param>
        /// <returns>Set of subscription.</returns>
        public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(string sourceRepo = null, string targetRepo = null, int? channelId = null)
        {
            return await _barClient.Subscriptions.ListSubscriptionsAsync(
                sourceRepository: sourceRepo,
                targetRepository: targetRepo,
                channelId: channelId);
        }

        /// <summary>
        /// Trigger a subscription by ID
        /// </summary>
        /// <param name="subscriptionId">ID of subscription to trigger</param>
        /// <returns>Subscription just triggered.</returns>
        public Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId)
        {
            return _barClient.Subscriptions.TriggerSubscriptionAsync(subscriptionId);
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
        ///     Delete a subscription by ID.
        /// </summary>
        /// <param name="subscriptionId">Id of subscription to delete.</param>
        /// <returns>Information on deleted subscription</returns>
        public Task<Subscription> DeleteSubscriptionAsync(Guid subscriptionId)
        {
            return _barClient.Subscriptions.DeleteSubscriptionAsync(subscriptionId);
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
        public async Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string repoUri = null, string branch = null)
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
            await _barClient.Repository.SetMergePoliciesAsync(repository: repoUri, branch: branch, body: mergePolicies.ToImmutableList());
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
        public async Task<IEnumerable<Asset>> GetAssetsAsync(string name = null,
                                                 string version = null,
                                                 int? buildId = null,
                                                 bool? nonShipping = null)
        {
            PagedResponse<Asset> pagedResponse = await _barClient.Assets.ListAssetsAsync(name: name,
                version: version, buildId: buildId, loadLocations: true);
            return await pagedResponse.EnumerateAll().ToListAsync(CancellationToken.None);
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
            var pagedResponse = await _barClient.Builds.ListBuildsAsync(repository: repoUri,
                commit: commit, loadCollections: true);
            return await pagedResponse.EnumerateAll().ToListAsync(CancellationToken.None);
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
        public async Task<Channel> GetChannelAsync(string channel)
        {
            return (await _barClient.Channels.ListChannelsAsync())
                .FirstOrDefault(c => c.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Retrieve a specific channel by id.
        /// </summary>
        /// <param name="channel">Channel id.</param>
        /// <returns>Channel or null if not found.</returns>
        public async Task<Channel> GetChannelAsync(int channel)
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
        public async Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null)
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
        public Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
        {
            return _barClient.Builds.GetLatestAsync(repository: repoUri,
                                                    channelId: channelId,
                                                    loadCollections: true);
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
    }
}
