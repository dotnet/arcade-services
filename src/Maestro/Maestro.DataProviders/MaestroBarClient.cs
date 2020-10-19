// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Maestro.DataProviders
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
                query = query.Where(dc => dc.Channel.Name == channel);
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
                other.Classification);
        }

        private const int EngLatestChannelId = 2;
        private const int Eng3ChannelId = 344;

        public async Task<DependencyFlowGraph> GetDependencyFlowGraphAsync(
            int channelId,
            int days,
            bool includeArcade,
            bool includeBuildTimes,
            bool includeDisabledSubscriptions,
            IReadOnlyList<string> includedFrequencies)
        {
            var engLatestChannel = await GetChannelAsync(EngLatestChannelId);
            var eng3Channel = await GetChannelAsync(Eng3ChannelId);
            var defaultChannels = (await GetDefaultChannelsAsync()).ToList();

            if (includeArcade)
            {
                if (engLatestChannel != null)
                {
                    defaultChannels.Add(
                        new DefaultChannel(0, "https://github.com/dotnet/arcade", true)
                        {
                            Branch = "master",
                            Channel = engLatestChannel
                        }
                    );
                }

                if (eng3Channel != null)
                {
                    defaultChannels.Add(
                        new DefaultChannel(0, "https://github.com/dotnet/arcade", true)
                        {
                            Branch = "release/3.x",
                            Channel = eng3Channel
                        }
                    );
                }
            }

            var subscriptions = (await GetSubscriptionsAsync()).ToList();

            // Build, then prune out what we don't want to see if the user specified
            // channels.
            DependencyFlowGraph flowGraph = await DependencyFlowGraph.BuildAsync(
                defaultChannels, 
                subscriptions,
                this,
                days);

            IEnumerable<string> frequencies
                = includedFrequencies == default || includedFrequencies.Count() == 0
                ? new string[] { "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", }
                : includedFrequencies;

            Channel targetChannel = null;

            if (channelId != 0)
            {
                targetChannel = await GetChannelAsync(channelId);
            }

            if (targetChannel != null)
            {
                flowGraph.PruneGraph(
                    node => DependencyFlowGraph.IsInterestingNode(targetChannel.Name, node),
                    edge => DependencyFlowGraph.IsInterestingEdge(edge, includeDisabledSubscriptions, frequencies));
            }

            if (includeBuildTimes)
            {
                var edgesWithLastBuild = flowGraph.Edges
                    .Where(e => e.Subscription.LastAppliedBuild != null);

                foreach (var edge in edgesWithLastBuild)
                {
                    edge.IsToolingOnly = !_context.IsProductDependency(
                        edge.From.Repository,
                        edge.From.Branch,
                        edge.To.Repository,
                        edge.To.Branch);
                }

                flowGraph.MarkBackEdges();
                flowGraph.CalculateLongestBuildPaths();
                flowGraph.MarkLongestBuildPath();
            }

            return flowGraph;
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
                    Policy = ToClientModelSubscriptionPolicy(other.PolicyObject),
                    LastAppliedBuild = other.LastAppliedBuild != null ? ToClientModelBuild(other.LastAppliedBuild) : null
                };
        }

        private Build ToClientModelBuild(Data.Models.Build other)
        {
            var channels = other.BuildChannels?
                .Select(bc => ToClientModelChannel(bc.Channel))
                .ToImmutableList();

            var assets = other.Assets?
                .Select(a => new Asset(a.Id, a.BuildId, a.NonShipping, a.Name, a.Version, null))
                .ToImmutableList();

            var dependencies = other.DependentBuildIds?
                .Select(ToClientModelBuildDependency)
                .ToImmutableList();

            var incoherences = other.Incoherencies?
                .Select(ToClientModelBuildIncoherence)
                .ToImmutableList();

            return new Build(
                other.Id,
                other.DateProduced,
                other.Staleness,
                other.Released,
                other.Stable,
                other.Commit,
                channels,
                assets,
                dependencies,
                incoherences);
        }

        private BuildRef ToClientModelBuildDependency(Data.Models.BuildDependency other)
        {
            return new BuildRef(other.BuildId, other.IsProduct, other.TimeToInclusionInMinutes);
        }

        private SubscriptionPolicy ToClientModelSubscriptionPolicy(Data.Models.SubscriptionPolicy other)
        {
            return new SubscriptionPolicy(
                other.Batchable,
                (UpdateFrequency) other.UpdateFrequency
            );
        }

        private BuildIncoherence ToClientModelBuildIncoherence(Data.Models.BuildIncoherence other)
        {
            return new BuildIncoherence
            {
                Commit = other.Commit,
                Name = other.Name,
                Repository = other.Repository,
                Version = other.Version
            };
        }

        public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(string sourceRepo = null, string targetRepo = null, int? channelId = null)
        {
            IQueryable<Data.Models.Subscription> query = _context.Subscriptions
                .Include(s => s.Channel)
                .Include(s => s.LastAppliedBuild);

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

        public Task<Subscription> TriggerSubscriptionAsync(Guid subscriptionId, int sourceBuildId)
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

        public async Task<Build> GetBuildAsync(int buildId)
        {
            var build = await _context.Builds.Where(b => b.Id == buildId)
                .Include(b => b.BuildChannels)
                .ThenInclude(bc => bc.Channel)
                .Include(b => b.Assets)
                .FirstOrDefaultAsync();

            if (build != null)
            {
                return ToClientModelBuild(build);
            }
            else
            {
                return null;
            }
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

        public async Task<Channel> GetChannelAsync(int channelId)
        {
            Data.Models.Channel channel = await _context.Channels
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
                return new BuildTime
                {
                    DefaultChannelId = 0,
                    OfficialBuildTime = 0,
                    PrBuildTime = 0,
                    GoalTimeInMinutes = 0
                };
            }

            MultiProjectKustoQuery queries = SharedKustoQueries.CreateBuildTimesQueries(defaultChannel.Repository, defaultChannel.Branch, days);

            var results = await Task.WhenAll<IDataReader>(_kustoClientProvider.ExecuteKustoQueryAsync(queries.Internal), 
                _kustoClientProvider.ExecuteKustoQueryAsync(queries.Public));

            (int officialBuildId, TimeSpan officialBuildTime) = SharedKustoQueries.ParseBuildTime(results[0]);
            (int prBuildId, TimeSpan prBuildTime) = SharedKustoQueries.ParseBuildTime(results[1]);

            double officialTime = 0;
            double prTime = 0;
            int goalTime = 0;

            if (officialBuildId != -1)
            {
                officialTime = officialBuildTime.TotalMinutes;
                
                // Get goal time for definition id
                Data.Models.GoalTime goal = await _context.GoalTime
                    .FirstOrDefaultAsync(g => g.DefinitionId == officialBuildId && g.ChannelId == defaultChannel.ChannelId);

                if (goal != null)
                {
                    goalTime = goal.Minutes;
                }
            }

            if (prBuildId != -1)
            {
                prTime = prBuildTime.TotalMinutes;
            }

            return new BuildTime
            {
                DefaultChannelId = defaultChannelId,
                OfficialBuildTime = officialTime,
                PrBuildTime = prTime,
                GoalTimeInMinutes = goalTime
            };
        }
    }
}
