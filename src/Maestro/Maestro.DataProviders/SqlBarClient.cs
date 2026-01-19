// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.DataProviders.Exceptions;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore;
using Channel = Microsoft.DotNet.ProductConstructionService.Client.Models.Channel;
using DefaultChannel = Microsoft.DotNet.ProductConstructionService.Client.Models.DefaultChannel;
using RepositoryBranch = Microsoft.DotNet.ProductConstructionService.Client.Models.RepositoryBranch;
using Subscription = Microsoft.DotNet.ProductConstructionService.Client.Models.Subscription;

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

    public static DefaultChannel ToClientModelDefaultChannel(Data.Models.DefaultChannel other)
    {
        return new DefaultChannel(other.Id, other.Repository, other.Enabled)
        {
            Branch = other.Branch,
            Channel = ToClientModelChannel(other.Channel)
        };
    }

    public static RepositoryBranch ToClientModelRepositoryBranch(Data.Models.RepositoryBranch other)
    {
        return new RepositoryBranch
        {
            Repository = other.RepositoryName,
            Branch = other.BranchName,
            MergePolicies = [.. (other.PolicyObject?.MergePolicies ?? [])
            .Select(p => new MergePolicy
            {
                Name = p.Name,
                Properties = p.Properties ?? [],
            })],
        };
    }
    public static Channel ToClientModelChannel(Data.Models.Channel other)
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

    public static Subscription ToClientModelSubscription(Data.Models.Subscription other)
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

    public async Task CreateSubscriptionsAsync(
        IEnumerable<Data.Models.Subscription> subscriptionsToCreate,
        bool andSaveContext = true)
    {
        var existingSubscriptions = await _context.Subscriptions.ToListAsync();

        var existingSubscriptionHashes = new HashSet<string>(
            existingSubscriptions.Select(SubscriptionComparisonKey));

        var newSubscriptionHashes = new HashSet<string>();

        foreach (var subscription in subscriptionsToCreate)
        {
            string subscriptionHash = SubscriptionComparisonKey(subscription);
            if (existingSubscriptionHashes.Contains(subscriptionHash))
            {
                throw new EntityConflictException($"Could not create subscription with id `{subscription.Id}`. "
                    + $"There already exists a subscription that performs the same update.");
            }

            if (newSubscriptionHashes.Contains(subscriptionHash))
            {
                throw new EntityConflictException($"Could not create subscription with id `{subscription.Id}`. "
                    + $"There is another subscription in the set of subscriptions to create that performs the same update.");
            }

            // Check for codeflow subscription conflicts
            var conflictError = await ValidateCodeflowSubscriptionConflicts(subscription);
            if (conflictError != null)
            {
                throw new InvalidOperationException($"Subscription {subscription.Id} creation failed with error {conflictError}");
            }

            _context.Subscriptions.Add(subscription);

            newSubscriptionHashes.Add(SubscriptionComparisonKey(subscription));
        }

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    ///     Validates codeflow subscription conflicts
    /// </summary>
    /// <param name="subscription">Subscription to validate</param>
    /// <returns>Error message if conflict found, null if no conflicts</returns>
    private async Task<string> ValidateCodeflowSubscriptionConflicts(Maestro.Data.Models.Subscription subscription)
    {
        if (!subscription.SourceEnabled)
        {
            return null;
        }

        // Check for backflow conflicts (source directory not empty)
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            var conflictingBackflowSubscription = await FindConflictingBackflowSubscription(subscription);
            if (conflictingBackflowSubscription != null)
            {
                return $"A backflow subscription '{conflictingBackflowSubscription.Id}' already exists for the same target repository and branch. " +
                       "Only one backflow subscription is allowed per target repository and branch combination.";
            }
        }

        // Check for forward flow conflicts (target directory not empty)
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            var conflictingForwardFlowSubscription = await FindConflictingForwardFlowSubscription(subscription);
            if (conflictingForwardFlowSubscription != null)
            {
                return $"A forward flow subscription '{conflictingForwardFlowSubscription.Id}' already exists for the same VMR repository, branch, and target directory. " +
                       "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.";
            }
        }

        return null;
    }

    /// <summary>
    ///     Find a conflicting backflow subscription (different subscription targeting same repo/branch)
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Conflicting subscription if found, null otherwise</returns>
    private async Task<Data.Models.Subscription> FindConflictingBackflowSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription) =>
        await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceEnabled == true
                && !string.IsNullOrEmpty(sub.SourceDirectory) // Backflow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.Id != updatedOrNewSubscription.Id);

    /// <summary>
    ///     Find a conflicting forward flow subscription (different subscription targeting same VMR branch/directory)
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Conflicting subscription if found, null otherwise</returns>
    private async Task<Data.Models.Subscription> FindConflictingForwardFlowSubscription(Maestro.Data.Models.Subscription updatedOrNewSubscription) =>
        await _context.Subscriptions.FirstOrDefaultAsync(sub =>
            sub.SourceEnabled == true
                && !string.IsNullOrEmpty(sub.TargetDirectory) // Forward flow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);

    public async Task UpdateSubscriptionsAsync(
        IEnumerable<Data.Models.Subscription> subscriptions,
        bool andSaveContext = true)
    {
        List<Guid> ids = [.. subscriptions.Select(sub => sub.Id)];

        var existingSubscriptions = await _context.Subscriptions
            .Where(sub => ids.Contains(sub.Id))
            .ToDictionaryAsync(sub => sub.Id);

        foreach (var subscription in subscriptions)
        {
            if (!existingSubscriptions.TryGetValue(
                subscription.Id,
                out Data.Models.Subscription existingSubscription))
            {
                throw new InvalidOperationException($"Failed to update subscription with id {subscription.Id} "
                    + "because the subscription could not be found in the database.");
            }

            await UpdateSubscriptionAsync(
                subscription,
                existingSubscription);
        }

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }

    private async Task UpdateSubscriptionAsync(
        Data.Models.Subscription subscription,
        Data.Models.Subscription existingSubscription)
    {
        List<string> illegalFieldChanges = [];

        if (subscription.TargetBranch != existingSubscription.TargetBranch)
        {
            illegalFieldChanges.Add("TargetBranch");
        }

        if (subscription.TargetRepository != existingSubscription.TargetRepository)
        {
            illegalFieldChanges.Add("TargetRepository");
        }

        if (subscription.PolicyObject.Batchable != existingSubscription.PolicyObject.Batchable)
        {
            illegalFieldChanges.Add("Batchable");
        }

        if (illegalFieldChanges.Count > 0)
        {
            throw new ArgumentException($"Subscription update failed for subscription {subscription.Id} because there was an " +
                $"attempt to modify the following immutable fields: {string.Join(", ", illegalFieldChanges)}");
        }

        var updatedFilters = ComputeUpdatedFilters(
            existingSubscription.ExcludedAssets,
            subscription.ExcludedAssets);

        existingSubscription.SourceRepository = subscription.SourceRepository;
        existingSubscription.TargetRepository = subscription.TargetRepository;
        existingSubscription.Enabled = subscription.Enabled;
        existingSubscription.SourceEnabled = subscription.SourceEnabled;
        existingSubscription.SourceDirectory = subscription.SourceDirectory;
        existingSubscription.TargetDirectory = subscription.TargetDirectory;
        existingSubscription.PolicyObject = subscription.PolicyObject;
        existingSubscription.PullRequestFailureNotificationTags = subscription.PullRequestFailureNotificationTags;
        existingSubscription.Channel = subscription.Channel;
        existingSubscription.ExcludedAssets = updatedFilters;

        var conflictError = await ValidateCodeflowSubscriptionConflicts(subscription);
        if (conflictError != null)
        {
            throw new InvalidOperationException($"Subscription {subscription.Id} update failed with error {conflictError}");
        }
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

    private static List<Data.Models.AssetFilter> ComputeUpdatedFilters(
        IEnumerable<Data.Models.AssetFilter> existingFilters,
        IEnumerable<Data.Models.AssetFilter> incomingFilters)
    {
        List<Data.Models.AssetFilter> result = [];

        Dictionary<string, Data.Models.AssetFilter> existingFilterLookups = existingFilters?
            .ToDictionary(a => a.Filter, StringComparer.OrdinalIgnoreCase)
            ?? [];

        foreach (var asset in incomingFilters)
        {
            if (existingFilterLookups.TryGetValue(asset.Filter, out var existingAsset))
            {
                result.Add(existingAsset);
            }
            else
            {
                result.Add(asset);
            }
        }

        return result;
    }

    /// <summary>
    /// Generates key representing the functional attributes of a subscription for comparison purposes.
    /// If two subscriptions produce the same hash, they are considered functionally equivalent
    /// </summary>
    private static string SubscriptionComparisonKey(Data.Models.Subscription subscription)
    {
        if (subscription.SourceEnabled)
        {
            // For source-enabled subs, the channel doesn't matter - we can only have one flow
            // between a product repo and a VMR directory on a given branch
            return string.Join(
                "|", new string[] {
                    subscription.SourceRepository,
                    subscription.TargetBranch,
                    subscription.TargetDirectory,
                    subscription.TargetRepository,
                    subscription.SourceDirectory,
                    subscription.SourceEnabled.ToString(),
            });
        }
        else
        {
            return string.Join(
                "|", new string[] {
                    subscription.Channel.Name,
                    subscription.SourceRepository,
                    subscription.TargetBranch,
                    subscription.TargetRepository,
                    subscription.TargetDirectory,
                    subscription.SourceEnabled.ToString(),
            });
        }
    }

    public async Task DeleteNamespaceAsync(string namespaceName, bool andSaveContext = true)
    {
        var barNamespace = await _context.Namespaces
            .Include(n => n.Subscriptions)
            .ThenInclude(s => s.ExcludedAssets)
            .Include(n => n.Channels)
            .Include(n => n.DefaultChannels)
            .Include(n => n.RepositoryBranches)
            .FirstOrDefaultAsync(n => n.Name == namespaceName);

        if (barNamespace == null)
        {
            throw new InvalidOperationException($"Namespace '{namespaceName}' not found.");
        }

        var subscriptionIds = barNamespace.Subscriptions.Select(sub => sub.Id).ToHashSet();

        _context.SubscriptionUpdates.RemoveRange(
            _context.SubscriptionUpdates
                .Where(s => subscriptionIds.Contains(s.SubscriptionId)));
        _context.AssetFilters.RemoveRange(
            barNamespace.Subscriptions.SelectMany(s => s.ExcludedAssets));
        _context.Channels.RemoveRange(barNamespace.Channels);
        _context.DefaultChannels.RemoveRange(barNamespace.DefaultChannels);
        _context.Subscriptions.RemoveRange(barNamespace.Subscriptions);
        _context.RepositoryBranches.RemoveRange(barNamespace.RepositoryBranches);
        _context.Namespaces.Remove(barNamespace);

        if (andSaveContext)
        {
            await _context.SaveChangesAsync();
        }
    }
}
