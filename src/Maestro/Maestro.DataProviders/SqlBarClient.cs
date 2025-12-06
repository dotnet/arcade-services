// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestor;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Octokit;

namespace Maestro.DataProviders;

/// <summary>
///     A bar client interface implementation used by all services which talks directly to the database.
/// </summary>
public class SqlBarClient : ISqlBarClient
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
            sub.TargetDirectory,
            sub.PullRequestFailureNotificationTags,
            [.. sub.ExcludedAssets.Select(s => s.Filter)]);
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
            other.Locations?.Select(ToClientAssetLocation).ToList());

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
                ? new string[] { "everyMonth", "everyTwoWeeks", "everyWeek", "twiceDaily", "everyDay", "everyBuild", "none", }
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
            other.TargetDirectory,
            other.ExcludedAssets?.Select(a => a.Filter).ToList())
        {
            Channel = ToClientModelChannel(other.Channel),
            Policy = ToClientModelSubscriptionPolicy(other.PolicyObject),
            LastAppliedBuild = other.LastAppliedBuild != null ? ToClientModelBuild(other.LastAppliedBuild) : null,
        };
    }

    public static Build ToClientModelBuild(Data.Models.Build other)
    {
        var channels = other.BuildChannels?
            .Select(bc => ToClientModelChannel(bc.Channel))
            .ToList();

        var assets = other.Assets?
            .Select(ToClientModelAsset)
            .ToList();

        var dependencies = other.DependentBuildIds?
            .Select(ToClientModelBuildDependency)
            .ToList();

        var incoherences = other.Incoherencies?
            .Select(ToClientModelBuildIncoherence)
            .ToList();

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
            AzureDevOpsAccount = other.AzureDevOpsAccount,
            AzureDevOpsProject = other.AzureDevOpsProject,
            AzureDevOpsBuildNumber = other.AzureDevOpsBuildNumber,
            AzureDevOpsBuildDefinitionId = other.AzureDevOpsBuildDefinitionId,
            AzureDevOpsBuildId = other.AzureDevOpsBuildId,
        };
    }

    private static BuildRef ToClientModelBuildDependency(Data.Models.BuildDependency other)
        => new(other.BuildId, other.IsProduct, other.TimeToInclusionInMinutes);

    private static SubscriptionPolicy ToClientModelSubscriptionPolicy(Data.Models.SubscriptionPolicy other)
        => new(other.Batchable, (UpdateFrequency)other.UpdateFrequency);

    public async Task<IEnumerable<Subscription>> GetSubscriptionsAsync(
        string sourceRepo = null, 
        string targetRepo = null, 
        int? channelId = null,
        bool? sourceEnabled = null,
        string sourceDirectory = null,
        string targetDirectory = null)
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

        if (sourceEnabled.HasValue)
        {
            query = query.Where(sub => sub.SourceEnabled == sourceEnabled.Value);
        }

        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            query = query.Where(sub => sub.SourceDirectory == sourceDirectory);
        }

        if (!string.IsNullOrEmpty(targetDirectory))
        {
            query = query.Where(sub => sub.TargetDirectory == targetDirectory);
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
            .ThenInclude(b => b.Locations)
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

    public async Task RegisterSubscriptionUpdate(
        Guid subscriptionId,
        string updateMessage)
    {
        Data.Models.Subscription subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        Data.Models.SubscriptionUpdate subscriptionUpdate = new()
        {
            SubscriptionId = subscription.Id,
            Subscription = subscription,
            Action = updateMessage
        };
        var existingSubscriptionUpdate = await _context.SubscriptionUpdates.FindAsync(subscriptionUpdate.SubscriptionId);
        if (existingSubscriptionUpdate == null)
        {
            _context.SubscriptionUpdates.Add(subscriptionUpdate);
        }
        else
        {
            _context.Entry(existingSubscriptionUpdate).CurrentValues.SetValues(subscriptionUpdate);
        }
        await _context.SaveChangesAsync();
    }

    public async Task<ConfigurationData> FetchExistingConfigurationDataAsync(string configurationNamespace)
    {
        var subscriptionsFetch = _context.Subscriptions
            .Where(sub => sub.Namespace.Name == configurationNamespace)
            .ToListAsync();

        var channelsFetch = _context.Channels
            .Where(c => c.Namespace.Name == configurationNamespace)
            .ToListAsync();

        var defaultChannelsFetch = _context.DefaultChannels
            .Where(dc => dc.Namespace.Name == configurationNamespace)
            .ToListAsync();

        var repositoryBranchesFetch = _context.RepositoryBranches
            .Where(rb => rb.Namespace.Name == configurationNamespace)
            .ToListAsync();

        await Task.WhenAll(subscriptionsFetch, channelsFetch, defaultChannelsFetch, repositoryBranchesFetch);

        return new ConfigurationData(
            subscriptionsFetch.Result,
            channelsFetch.Result,
            defaultChannelsFetch.Result,
            repositoryBranchesFetch.Result);
    }

    public async Task CreateSubscriptionsAsync(IEnumerable<Data.Models.Subscription> subscriptionsToCreate, bool andSaveContext = true)
    {
        foreach (var subscription in subscriptionsToCreate)
        {
            await CreateSubscriptionAsync(subscription, false);
        }
        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task CreateSubscriptionAsync(Data.Models.Subscription subscription, bool andSaveContext = true)
    {
        await ValidateSubscriptionConflicts(subscription);

        _context.Subscriptions.Add(subscription);

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateSubscriptionsAsync(IEnumerable<Data.Models.Subscription> subscriptionsToUpdate, bool andSaveContext = true)
    {
        foreach (var subscription in subscriptionsToUpdate)
        {
            await UpdateSubscriptionAsync(subscription, false);
        }
        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateSubscriptionAsync(Data.Models.Subscription subscription, bool andSaveContext = true)
    {
        //todo check if it's better to remove .AsNoTracking() because we might already have tracked entities here
        var existingSubscription = await _context.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subscription.Id);

        List<string> illegalFieldChanges = [];

        if (subscription.TargetBranch != existingSubscription.TargetBranch)
        {
            illegalFieldChanges.Add("TargetBranch");
        }

        if (subscription.TargetRepository != existingSubscription.TargetRepository)
        {
            illegalFieldChanges.Add("TargetRepository");
        }

        if (subscription.SourceRepository != existingSubscription.SourceRepository)
        {
            illegalFieldChanges.Add("SourceRepository");
        }

        if (subscription.PolicyObject.Batchable != existingSubscription.PolicyObject.Batchable)
        {
            illegalFieldChanges.Add("Batchable");
        }

        if (illegalFieldChanges.Count > 0)
        {
            throw new ArgumentException($"Subscription update failed for subscription {subscription.Id} because there was an " +
                $"attempt to modify the following immutable fields: {subscription.Id}: {string.Join(", ", illegalFieldChanges)}");
        }

        existingSubscription.SourceRepository = existingSubscription.SourceRepository;
        existingSubscription.TargetRepository = existingSubscription.TargetRepository;
        existingSubscription.Enabled = existingSubscription.Enabled;
        existingSubscription.SourceEnabled = existingSubscription.SourceEnabled;
        existingSubscription.SourceDirectory = existingSubscription.SourceDirectory;
        existingSubscription.TargetDirectory = existingSubscription.TargetDirectory;
        existingSubscription.PolicyObject = existingSubscription.PolicyObject;
        existingSubscription.PullRequestFailureNotificationTags = existingSubscription.PullRequestFailureNotificationTags;
        existingSubscription.Channel = subscription.Channel;
        // todo: Excluded assets need an ID (?)

        _context.Subscriptions.Update(existingSubscription);
    }

    public async Task DeleteSubscriptionsAsync(
        IEnumerable<Data.Models.Subscription> subscriptionsToDelete, bool andSaveContext = true)
    {
        var subscriptionLookups = subscriptionsToDelete.ToDictionary(s => s.Id);

        _context.SubscriptionUpdates.RemoveRange(
            _context.SubscriptionUpdates
            .Where(s => subscriptionLookups.ContainsKey(s.SubscriptionId)));

        _context.Subscriptions.RemoveRange(
            subscriptionLookups.Values
            .Where(s => subscriptionLookups.ContainsKey(s.Id)));

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateChannelsAsync(IEnumerable<Data.Models.Channel> channelsToUpdate, bool andSaveContext = true)
    {
        foreach (var channel in channelsToUpdate)
        {
            await UpdateChannelAsync(channel, false);
        }

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateChannelAsync(Data.Models.Channel channel, bool andSaveContext = true)
    {
        var existingChannel = await _context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UniqueId == channel.UniqueId);

        existingChannel.Classification = channel.Classification;
        _context.Channels.Update(existingChannel);

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateDefaultChannelsAsync(IEnumerable<Data.Models.DefaultChannel> defaultChannels, bool andSaveContext = true)
    {
        foreach (var defaultChannel in defaultChannels)
        {
            await UpdateDefaultChannelAsync(defaultChannel, false);
        }
        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateDefaultChannelAsync(Data.Models.DefaultChannel defaultChannel, bool andSaveContext = true)
    {
        var existingDefaultChannel = await _context.DefaultChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(dc => dc.Id == defaultChannel.Id);

        List<string> illegalFieldChanges = [];

        if (defaultChannel.ChannelId != existingDefaultChannel.ChannelId)
        {
            illegalFieldChanges.Add("ChannelId");
        }

        if (illegalFieldChanges.Count > 0)
        {
            throw new ArgumentException($"Subscription update failed for subscription {subscription.Id} because there was an " +
                $"attempt to modify the following immutable fields: {subscription.Id}: {string.Join(", ", illegalFieldChanges)}");
        }

        defaultChannel.Enabled = defaultChannel.Enabled;
        _context.DefaultChannels.Update(defaultChannel);
    }

    private async Task ValidateSubscriptionConflicts(
        Data.Models.Subscription subscription)
    {
        if (subscription.SourceEnabled)
        {
            await ValidateConflictingCodeflowSubscriptions(subscription);
        }
        else
        {
            await ValidateConflictingDependencySubscriptions(subscription);
        }
    }
    private async Task ValidateConflictingDependencySubscriptions(Data.Models.Subscription subscription)
    {
            var equivalentSub = await _context.Subscriptions.FirstOrDefaultAsync(sub =>
                sub.SourceRepository == subscription.SourceRepository
                    && sub.ChannelId == subscription.Channel.Id
                    && sub.TargetRepository == subscription.TargetRepository
                    && sub.TargetBranch == subscription.TargetBranch
                    && sub.SourceEnabled == subscription.SourceEnabled
                    && sub.SourceDirectory == subscription.SourceDirectory
                    && sub.TargetDirectory == subscription.TargetDirectory
                    && sub.Id != subscription.Id);

        if (equivalentSub != null)
        {
            throw new ArgumentException($"Could not create or update subscription with id `{subscription.Id}`. "
                + $"There already exists a subscription that performs the same update. (`{equivalentSub.Id}`).");
        }

    }

    private async Task ValidateConflictingCodeflowSubscriptions(
        Data.Models.Subscription subscription)
    {
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            var equivalentFlow = await _context.Subscriptions.FirstOrDefaultAsync(s =>
                s.SourceEnabled == true
                    && !string.IsNullOrEmpty(s.TargetDirectory)
                    && s.TargetRepository == subscription.TargetRepository
                    && s.TargetBranch == subscription.TargetBranch
                    && s.TargetDirectory == subscription.TargetDirectory
                    && s.Id != subscription.Id);

            if (equivalentFlow != null)
            {
                throw new ArgumentException(
                $"A forward flow subscription '{equivalentFlow.Id}' already exists for the same VMR repository, branch, and target directory. "
                + "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.");
            }
        }

        else if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            var equivalentFlow = await _context.Subscriptions.FirstOrDefaultAsync(s =>
                s.SourceEnabled == true
                    && !string.IsNullOrEmpty(s.SourceDirectory)
                    && s.SourceRepository == subscription.SourceRepository
                    && s.TargetBranch == subscription.TargetBranch
                    && s.SourceDirectory == subscription.SourceDirectory
                    && s.Id != subscription.Id);

            if (equivalentFlow != null)
            {
                throw new ArgumentException(
                    $"A backflow subscription '{equivalentFlow.Id}' already exists for the same target repository and branch. " +
                    "Only one backflow subscription is allowed per target repository and branch combination.");
            }
        }
    }
}
