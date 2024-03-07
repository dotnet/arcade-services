// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

namespace Maestro.DataProviders;

/// <summary>
///     A bar client interface implementation used by all services which talks directly to the database.
/// </summary>
public class SqlBarClient : IBasicBarClient
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IKustoClientProvider _kustoClientProvider;

    public SqlBarClient(
        BuildAssetRegistryContext context,
        IKustoClientProvider kustoClientProvider)
    {
        _context = context;
        _kustoClientProvider = kustoClientProvider;
    }

    public async Task<Subscription> GetSubscriptionAsync(Guid subscriptionId)
    {
        var sub = await _context.Subscriptions
            .Include(s => s.ExcludedAssets)
            .FirstOrDefaultAsync(s => s.Id.Equals(subscriptionId));

        if (sub == null)
        {
            return null;
        }

        return new Subscription(
            sub.Id,
            sub.Enabled,
            sub.SourceEnabled,
            sub.SourceRepository,
            sub.TargetRepository,
            sub.TargetBranch,
            sub.SourceDirectory,
            sub.PullRequestFailureNotificationTags,
            sub.ExcludedAssets.Select(s => s.Filter).ToImmutableList());
    }

    public async Task<Subscription> GetSubscriptionAsync(string subscriptionId)
    {
        return await GetSubscriptionAsync(Guid.Parse(subscriptionId));
    }

    public async Task<Build> GetLatestBuildAsync(string repoUri, int channelId)
    {
        Data.Models.Build build = await _context.Builds
            .Where(b => (repoUri == b.GitHubRepository || repoUri == b.AzureDevOpsRepository) && b.BuildChannels.Any(c => c.ChannelId == channelId))
            .Include(b => b.Assets)
            .OrderByDescending(b => b.DateProduced)
            .FirstOrDefaultAsync();

        return build != null
            ? ToClientModelBuild(build)
            : null;
    }

    public async Task<IEnumerable<Build>> GetBuildsAsync(string repoUri, string commit)
    {
        List<Data.Models.Build> builds = await _context.Builds.Where(b =>
                (repoUri == b.AzureDevOpsRepository || repoUri == b.GitHubRepository) && (commit == b.Commit))
            .Include(b => b.Assets)
            .OrderByDescending(b => b.DateProduced)
            .ToListAsync();

        return builds.Select(ToClientModelBuild);
    }

    public async Task<IEnumerable<Asset>> GetAssetsAsync(
        string name = null,
        string version = null,
        int? buildId = null,
        bool? nonShipping = null)
    {
        IQueryable<Data.Models.Asset> assets = _context.Assets;
        if (name != null)
        {
            assets = assets.Where(a => a.Name == name);
        }
        if (version != null)
        {
            assets = assets.Where(a => a.Version == version);
        }
        if (buildId != null)
        {
            assets = assets.Where(a => a.BuildId == buildId);
        }
        if (nonShipping != null)
        {
            assets = assets.Where(a => a.NonShipping == nonShipping);
        }

        var assetList = await assets.Include(a => a.Locations)
            .OrderByDescending(a => a.BuildId)
            .ToListAsync();

        return assetList.Select(ToClientModelAsset);
    }

    private static AssetLocation ToClientAssetLocation(Data.Models.AssetLocation other)
    {
        return new AssetLocation(other.Id, (LocationType)other.Type, other.Location);
    }

    private static Asset ToClientModelAsset(Data.Models.Asset other)
        => new(
            other.Id,
            other.BuildId,
            other.NonShipping,
            other.Name,
            other.Version,
            other.Locations?.Select(ToClientAssetLocation).ToImmutableList());

    private static BuildIncoherence ToClientModelBuildIncoherence(Data.Models.BuildIncoherence incoherence)
        => new()
        {
            Name = incoherence.Name,
            Repository = incoherence.Repository,
            Version = incoherence.Version,
            Commit = incoherence.Commit
        };

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

        return defaultChannels.Select(ToClientModelDefaultChannel);
    }

    private DefaultChannel ToClientModelDefaultChannel(Data.Models.DefaultChannel other)
    {
        return new DefaultChannel(other.Id, other.Repository, other.Enabled)
        {
            Branch = other.Branch,
            Channel = ToClientModelChannel(other.Channel)
        };
    }

    private static Channel ToClientModelChannel(Data.Models.Channel other)
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
                        Branch = "main",
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
            = includedFrequencies == default || includedFrequencies.Count == 0
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

    private Subscription ToClientModelSubscription(Data.Models.Subscription other)
    {
        return new Subscription(
            other.Id,
            other.Enabled,
            other.SourceEnabled,
            other.SourceRepository,
            other.TargetRepository,
            other.TargetBranch,
            other.PullRequestFailureNotificationTags,
            other.SourceDirectory,
            other.ExcludedAssets?.Select(a => a.Filter).ToImmutableList())
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
            .Select(ToClientModelAsset)
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
            incoherences)
        {
            AzureDevOpsBranch = other.AzureDevOpsBranch,
            GitHubBranch = other.GitHubBranch,
            GitHubRepository = other.GitHubRepository,
            AzureDevOpsRepository = other.AzureDevOpsRepository,
        };
    }

    private BuildRef ToClientModelBuildDependency(Data.Models.BuildDependency other)
        => new(other.BuildId, other.IsProduct, other.TimeToInclusionInMinutes);

    private static SubscriptionPolicy ToClientModelSubscriptionPolicy(Data.Models.SubscriptionPolicy other)
        => new(other.Batchable, (UpdateFrequency)other.UpdateFrequency);

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

        return results.Select(ToClientModelSubscription);
    }

    public async Task<Build> GetBuildAsync(int buildId)
    {
        var build = await _context.Builds.Where(b => b.Id == buildId)
            .Include(b => b.BuildChannels)
            .ThenInclude(b => b.Channel)
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

    public async Task<BuildTime> GetBuildTimeAsync(int defaultChannelId, int days)
    {
        var defaultChannel = await _context.DefaultChannels
            .Where(dc => dc.Id == defaultChannelId)
            .Select(dc => new
            {
                Repository = dc.Repository,
                Branch = dc.Branch,
                ChannelId = dc.ChannelId,

                // Get AzDO BuildDefinitionId for the most recent build in the default channel.
                // It will be used to restrict the average build time query in Kusto
                // to official builds only.
                BuildDefinitionId = dc.Channel.BuildChannels
                    .Select(bc => bc.Build)
                    .Where(b => b.AzureDevOpsBuildDefinitionId.HasValue
                                && ((b.GitHubRepository == dc.Repository && b.GitHubBranch == dc.Branch)
                                    || (b.AzureDevOpsRepository == dc.Repository && b.AzureDevOpsBranch == dc.Branch)))
                    .OrderByDescending(b => b.DateProduced)
                    .Select(b => b.AzureDevOpsBuildDefinitionId)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (defaultChannel == null)
        {
            return null;
        }

        MultiProjectKustoQuery queries = SharedKustoQueries.CreateBuildTimesQueries(
            defaultChannel.Repository,
            defaultChannel.Branch,
            days,
            defaultChannel.BuildDefinitionId);

        var results = await Task.WhenAll(_kustoClientProvider.ExecuteKustoQueryAsync(queries.Internal),
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
