// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.TeamFoundation.Build.WebApi.Events;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Maestro.Web
{
    /// <summary>
    ///     A bar client interface for use by DarcLib which talks directly
    ///     to the database for diamond dependency resolution.  Only a few features are required.
    /// </summary>
    internal class MaestroBarClient : IBarClient
    {
        private readonly BuildAssetRegistryContext _context;
        private readonly KustoClientProvider _kustoClientProvider;

        public MaestroBarClient(BuildAssetRegistryContext context)
        {
            _context = context;
        }

        public MaestroBarClient(BuildAssetRegistryContext context,
                                IKustoClientProvider kustoClientProvider)
        {
            _context = context;
            _kustoClientProvider = (KustoClientProvider) kustoClientProvider;
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

        public async Task<IEnumerable<DefaultChannel>> GetDefaultChannelsAsync(string repository = null, string branch = null, string channel = null)
        {
            IQueryable<Data.Models.DefaultChannel> query = _context.DefaultChannels.Include(dc => dc.Channel)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(repository))
            {
                query = query.Where(dc => dc.Repository == repository);
            }

            if (!string.IsNullOrEmpty(branch))
            {
                // Normalize the branch name to not include refs/heads
                string normalizedBranchName = GitHelpers.NormalizeBranchName(branch);
                query = query.Where(dc => dc.Branch == normalizedBranchName);
            }

            if (!string.IsNullOrEmpty(channel))
            {
                query = query.Where(dc => dc.Channel.Name.Equals(channel, StringComparison.OrdinalIgnoreCase));
            }

            var defaultChannels = await query.ToListAsync();
            
            return defaultChannels.Select(dc => ToClientModelDefaultChannel(dc));
        }

        private DefaultChannel ToClientModelDefaultChannel(Maestro.Data.Models.DefaultChannel other)
        {
            return new DefaultChannel(other.Id, other.Repository, other.Enabled)
            {
                Branch = other.Branch,
                Channel = ToClientModelChannel(other.Channel)
            };
        }

        private Channel ToClientModelChannel(Maestro.Data.Models.Channel other)
        {
            return new Channel(
                other.Id, 
                other.Name, 
                other.Classification, 
                other.ChannelReleasePipelines?.Select(rp => ToClientModelReleasePipeline(rp)).ToImmutableList());
        }

        private ReleasePipeline ToClientModelReleasePipeline(Maestro.Data.Models.ChannelReleasePipeline other)
        {
            return new ReleasePipeline(other.ChannelId, other.ReleasePipelineId);
        }

        public Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
        {
            throw new NotImplementedException();
        }

        public Task<Subscription> GetSubscriptionAsync(Guid subscriptionId)
        {
            throw new NotImplementedException();
        }

        private Subscription ToClientModelSubscription(Maestro.Data.Models.Subscription other)
        {
            return new Subscription(
                other.Id, 
                other.Enabled, 
                other.SourceRepository, 
                other.TargetRepository, 
                other.TargetBranch)
                {
                    Channel = ToClientModelChannel(other.Channel),
                    Policy = ToClientModelSubscriptionPolicy(other.PolicyObject)
                };
        }
        
        private SubscriptionPolicy ToClientModelSubscriptionPolicy(Maestro.Data.Models.SubscriptionPolicy other)
        {
            return new SubscriptionPolicy(
                other.Batchable,
                (UpdateFrequency) other.UpdateFrequency
            );
        }

        public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(string sourceRepo = null, string targetRepo = null, int? channelId = null)
        {
            IQueryable<Data.Models.Subscription> query = _context.Subscriptions.Include(s => s.Channel);

            if (!string.IsNullOrEmpty(sourceRepo))
            {
                query = query.Where(sub => sub.SourceRepository == sourceRepo);
            }

            if (!string.IsNullOrEmpty(targetRepo))
            {
                query = query.Where(sub => sub.TargetRepository == targetRepo);
            }

            if (channelId.HasValue)
            {
                query = query.Where(sub => sub.ChannelId == channelId.Value);
            }

            List<Data.Models.Subscription> results = await query.ToListAsync();

            return results.Select(sub => ToClientModelSubscription(sub));
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

        public Task AssignBuildToChannelAsync(int buildId, int channelId)
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

        public Task<Build> GetBuildAsync(int buildId)
        {
            throw new NotImplementedException();
        }

        public Task<Build> UpdateBuildAsync(int buildId, BuildUpdate buildUpdate)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Asset>> GetAssetsAsync(
            string name = null,
            string version = null,
            int? buildId = null,
            bool? nonShipping = null)
        {
            throw new NotImplementedException();
        }

        private AssetLocation ToClientAssetLocation(Maestro.Data.Models.AssetLocation other)
        {
            return new AssetLocation(other.Id, (LocationType)other.Type, other.Location);
        }

        private Asset ToClientModelAsset(Maestro.Data.Models.Asset other)
        {
            return new Asset(
                other.Id,
                other.BuildId,
                other.NonShipping,
                other.Name,
                other.Version,
                other.Locations?.Select(l => ToClientAssetLocation(l)).ToImmutableList());
        }

        private Build ToClientModelBuild(Maestro.Data.Models.Build other)
        {
            return new Build(other.Id, other.DateProduced, other.Staleness, false, other.PublishUsingPipelines, other.Commit,
                null, other.Assets?.Select(a => ToClientModelAsset(a)).ToImmutableList(),
                other.DependentBuildIds?.Select(b => new BuildRef(b.BuildId, b.IsProduct, b.TimeToInclusionInMinutes)).ToImmutableList())
            {
                AzureDevOpsBranch = other.AzureDevOpsBranch,
                GitHubBranch = other.GitHubBranch,
                GitHubRepository = other.GitHubRepository,
                AzureDevOpsRepository = other.AzureDevOpsRepository,
            };
        }

        public async Task<Channel> GetChannelAsync(int channelId)
        {
            Data.Models.Channel channel = await _context.Channels
                .Include(ch => ch.ChannelReleasePipelines)
                .ThenInclude(crp => crp.ReleasePipeline)
                .Where(c => c.Id == channelId).FirstOrDefaultAsync();

            if (channel != null)
            {
                return ToClientModelChannel(channel);
            }
            
            return null;
        }

        ///  Unsupported method
        public Task<Goal> SetGoalAsync(string channel, int definitionId, int minutes)
        {
            throw new NotImplementedException();
        }

        ///  Unsupported method
        public Task<Goal> GetGoalAsync(string channel, int definitionId)
        {
            throw new NotImplementedException();
        }

        public Task DeleteBuildFromChannelAsync(int buildId, int channelId)
        {
            throw new NotImplementedException();
        }

        public async Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days)
        {
            Data.Models.DefaultChannel defaultChannel = await _context.DefaultChannels.FindAsync(defaultChannelId);

            if (defaultChannel == null)
            {
                return new BuildTime(0, 0, 0);
            }

            Dictionary<string, KustoQuery> queries = Helpers.CreateBuildTimesQueries(defaultChannel.Repository, defaultChannel.Branch, days);

            var results = await Task.WhenAll<TimeSpan>(_kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(queries["internal"]), 
                _kustoClientProvider.GetSingleValueFromQueryAsync<TimeSpan>(queries["public"]));

            double officialTime = results[0].TotalMinutes;
            double prTime = results[1].TotalMinutes;

            return new BuildTime(defaultChannelId, officialTime, prTime);
        }
    }
}
