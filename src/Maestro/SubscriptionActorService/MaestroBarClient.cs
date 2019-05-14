// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SubscriptionActorService
{
    /// <summary>
    ///     A bar client interface for use by DarcLib which talks directly
    ///     to the database for diamond dependency resolution.  Only a few features are required.
    /// </summary>
    internal class MaestroBarClient : IBarClient
    {
        private readonly BuildAssetRegistryContext _context;

        public MaestroBarClient(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        #region Unneeded APIs

        public Task AddDefaultChannelAsync(string repository, string branch, string channel)
        {
            throw new NotImplementedException();
        }

        public Task<Channel> CreateChannelAsync(string name, string classification)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> CreateSubscriptionAsync(string channelName, string sourceRepo, string targetRepo, string targetBranch,
            string updateFrequency, bool batchable, List<MergePolicy> mergePolicies)
        {
            throw new NotImplementedException();
        }

        public Task<Channel> DeleteChannelAsync(int id)
        {
            throw new NotImplementedException();
        }

        public Task DeleteDefaultChannelAsync(int id)
        {
            throw new NotImplementedException();
        }

        public Task UpdateDefaultChannelAsync(int id, string repository = null, string branch = null, string channel = null, bool? enabled = null)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> DeleteSubscriptionAsync(Guid subscriptionId)
        {
            throw new NotImplementedException();
        }

        public Task<Channel> GetChannelAsync(string channel)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Channel>> GetChannelsAsync(string classification = null)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(string repository = null, string branch = null, string channel = null)
        {
            throw new NotImplementedException();
        }

        public Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> GetSubscriptionAsync(Guid subscriptionId)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Subscription>> GetSubscriptionsAsync(string sourceRepo = null, string targetRepo = null, int? channelId = null)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> UpdateSubscriptionAsync(Guid subscriptionId, SubscriptionUpdate subscription)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<MergePolicy>> GetRepositoryMergePoliciesAsync(string repoUri, string branch)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Asset>> GetAssetsAsync(string name = null,
            string version = null, int? buildId = null, bool? nonShipping = null)
        {
            throw new NotImplementedException();
        }

        public Task AssignBuildToChannel(int buildId, int channelId)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<RepositoryBranch>> GetRepositoriesAsync(string repoUri = null, string branch = null)
        {
            throw new NotImplementedException();
        }

        public Task SetRepositoryMergePoliciesAsync(string repoUri, string branch, List<MergePolicy> mergePolicies)
        {
            throw new NotImplementedException();
        }

        #endregion

        /// <summary>
        ///     Get a list of builds for the given repo uri and commit.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Commit</param>
        /// <returns>Build with specific Id</returns>
        /// <remarks>This only implements the narrow needs of the dependency graph
        /// builder in context of coherency.  For example channels are not included./remarks>
        public async Task<Build> GetBuildAsync(int buildId)
        {
            Maestro.Data.Models.Build build = await _context.Builds.Where(b => b.Id == buildId)
                .Include(b => b.Assets)
                .FirstOrDefaultAsync();

            if (build == null)
            {
                throw new DarcException($"Could not find a build with id '{buildId}'");
            }

            return ToClientModelBuild(build);
        }

        /// <summary>
        ///     Get a list of builds for the given repo uri and commit.
        /// </summary>
        /// <param name="repoUri">Repository uri</param>
        /// <param name="commit">Commit</param>
        /// <returns>List of builds</returns>
        public async Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
        {
            List<Maestro.Data.Models.Build> builds = await _context.Builds.Where(b =>
                (repoUri == b.AzureDevOpsRepository || repoUri == b.GitHubRepository) && (commit == b.Commit))
                .Include(b => b.Assets)
                .OrderByDescending(b => b.DateProduced)
                .ToListAsync();

            return builds.Select(b => ToClientModelBuild(b));
        }

        private Asset ToClientModelAsset(Maestro.Data.Models.Asset other)
        {
            return new Asset(other.Id, other.BuildId, other.NonShipping,
                other.Name, other.Version, null);
        }

        private Build ToClientModelBuild(Maestro.Data.Models.Build other)
        {
            return new Build(other.Id, other.DateProduced, other.PublishUsingPipelines, other.Commit,
                null, other.Assets?.Select(a => ToClientModelAsset(a)).ToImmutableList(), null)
            {
                AzureDevOpsBranch = other.AzureDevOpsBranch,
                GitHubBranch = other.GitHubBranch,
                GitHubRepository = other.GitHubRepository,
                AzureDevOpsRepository = other.AzureDevOpsRepository,
            };
        }
    }
}
