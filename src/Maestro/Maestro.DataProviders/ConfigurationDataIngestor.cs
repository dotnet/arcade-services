// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

#nullable enable
namespace Maestro.DataProviders;

public interface IConfigurationDataIngestor
{
    Task ClearConfiguration(string repoUri, string branch);
    Task IngestConfiguration(string repoUri, string branch);
}

public class ConfigurationDataIngestor : IConfigurationDataIngestor
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IGitRepoFactory _gitRepoFactory;
    private readonly ILogger<ConfigurationDataIngestor> _logger;

    public ConfigurationDataIngestor(
        BuildAssetRegistryContext context,
        IGitRepoFactory gitRepoFactory,
        ILogger<ConfigurationDataIngestor> logger)
    {
        _context = context;
        _gitRepoFactory = gitRepoFactory;
        _logger = logger;
    }

    public async Task ClearConfiguration(string repoUri, string branch)
    {
        _logger.LogInformation("Starting to clear configuration for repository {RepoUri} on branch {Branch}", repoUri, branch);

        if (branch == "staging" || branch == "production")
        {
            var message = "Clearing the staging or production configuration is not allowed.";
            _logger.LogError("Configuration clear operation rejected: {Message} Repository: {RepoUri}, Branch: {Branch}", message, repoUri, branch);
            throw new InvalidOperationException(message);
        }

        var configurationSource = await _context.ConfigurationSources
            .Where(cs => cs.Uri == repoUri && cs.Branch == branch)
            .FirstOrDefaultAsync();

        if (configurationSource == null)
        {
            _logger.LogInformation("No configuration source found for repository {RepoUri} on branch {Branch}. Nothing to clear.", repoUri, branch);
            return;
        }

        _logger.LogInformation("Found configuration source {ConfigurationSourceId} for repository {RepoUri} on branch {Branch}. Proceeding with clear operation.", 
            configurationSource.Id, repoUri, branch);

        await IngestConfigurationInternal(repoUri, branch, configurationSource, [], [], []);
        _context.ConfigurationSources.Remove(configurationSource);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Successfully cleared configuration for repository {RepoUri} on branch {Branch}", repoUri, branch);
    }

    public async Task IngestConfiguration(string repoUri, string branch)
    {
        _logger.LogInformation("Starting configuration ingestion for repository {RepoUri} on branch {Branch}", repoUri, branch);

        try
        {
            IGitRepo repo = _gitRepoFactory.CreateClient(repoUri);

            _logger.LogDebug("Fetching configuration files from repository {RepoUri} on branch {Branch}", repoUri, branch);
            IReadOnlyList<GitFile> subscriptionFiles = await repo.GetFilesAsync(repoUri, branch, "subscriptions");
            IReadOnlyList<GitFile> channelFiles = await repo.GetFilesAsync(repoUri, branch, "channels");
            IReadOnlyList<GitFile> defaultChannelFiles = await repo.GetFilesAsync(repoUri, branch, "default-channels");

            _logger.LogInformation("Retrieved {SubscriptionFileCount} subscription files, {ChannelFileCount} channel files, {DefaultChannelFileCount} default channel files", 
                subscriptionFiles.Count, channelFiles.Count, defaultChannelFiles.Count);

            IDeserializer serializer = new DeserializerBuilder().Build();

            _logger.LogDebug("Deserializing configuration files");
            IReadOnlyCollection<SubscriptionUpdateYamlData> ingestedSubscriptions =
                [.. subscriptionFiles.SelectMany(f => serializer.Deserialize<List<SubscriptionUpdateYamlData>>(f.Content))];
            IReadOnlyList<ChannelYamlData> ingestedChannels =
                [.. channelFiles.SelectMany(f => serializer.Deserialize<List<ChannelYamlData>>(f.Content))];
            IReadOnlyList<DefaultChannelYamlData> ingestedDefaultChannels =
                [.. defaultChannelFiles.SelectMany(f => serializer.Deserialize<List<DefaultChannelYamlData>>(f.Content))];

            _logger.LogInformation("Deserialized {SubscriptionCount} subscriptions, {ChannelCount} channels, {DefaultChannelCount} default channels", 
                ingestedSubscriptions.Count, ingestedChannels.Count, ingestedDefaultChannels.Count);

            var configurationSource = await _context.ConfigurationSources
                .Where(cs => cs.Uri == repoUri && cs.Branch == branch)
                .FirstOrDefaultAsync();

            await IngestConfigurationInternal(
                repoUri,
                branch,
                configurationSource,
                ingestedChannels,
                ingestedDefaultChannels,
                ingestedSubscriptions);

            _logger.LogInformation("Successfully completed configuration ingestion for repository {RepoUri} on branch {Branch}", repoUri, branch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest configuration for repository {RepoUri} on branch {Branch}", repoUri, branch);
            throw;
        }
    }

    private async Task IngestConfigurationInternal(
        string repoUri,
        string branch,
        ConfigurationSource? configurationSource,
        IReadOnlyList<ChannelYamlData> ingestedChannels,
        IReadOnlyList<DefaultChannelYamlData> ingestedDefaultChannels,
        IReadOnlyCollection<SubscriptionUpdateYamlData> ingestedSubscriptions)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            if (configurationSource == null)
            {
                configurationSource = new ConfigurationSource
                {
                    Uri = repoUri,
                    Branch = branch
                };
                configurationSource = (await _context.ConfigurationSources.AddAsync(configurationSource)).Entity;
            }

            Dictionary<Guid, Subscription> existingSubscriptions = _context.Subscriptions
                .Where(sub => sub.ConfigurationSourceId == configurationSource.Id)
                .ToDictionary(sub => sub.Id);

            Dictionary<string, Channel> existingChannels = _context.Channels
                .Where(c => c.ConfigurationSourceId == configurationSource.Id)
                .ToDictionary(c => c.Name);

            List<DefaultChannel> existingDefaultChannels = _context.DefaultChannels
                .Where(dc => dc.ConfigurationSourceId == configurationSource.Id)
                .ToList();

            _logger.LogInformation("Found {ExistingSubscriptionCount} existing subscriptions, {ExistingChannelCount} existing channels, {ExistingDefaultChannelCount} existing default channels", 
                existingSubscriptions.Count, existingChannels.Count, existingDefaultChannels.Count);

            // Remove any subscriptions, channels, or default channels that are no longer present in the configuration
            _logger.LogInformation("Removing entities no longer present in configuration");
            RemoveSubscriptions(existingSubscriptions, ingestedSubscriptions);
            RemoveDefaultChannels(existingDefaultChannels, ingestedDefaultChannels);
            RemoveChannels(existingChannels, ingestedChannels);

            // Add or update any items
            _logger.LogInformation("Adding or updating entities from configuration");
            AddOrUpdateChannels(existingChannels, ingestedChannels, configurationSource.Id);
            AddOrUpdateDefaultChannels(existingDefaultChannels, ingestedDefaultChannels, existingChannels, configurationSource.Id);
            AddOrUpdateSubscriptions(existingSubscriptions, ingestedSubscriptions, existingChannels, configurationSource.Id);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            _logger.LogDebug("Successfully committed database transaction for configuration ingestion");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during internal configuration ingestion for repository {RepoUri} on branch {Branch}. Rolling back transaction.", repoUri, branch);
            // TODO: Handle failure
            throw;
        }
    }

    private void AddOrUpdateSubscriptions(
        Dictionary<Guid, Subscription> existingSubscriptions,
        IReadOnlyCollection<SubscriptionUpdateYamlData> ingestedSubscriptions,
        Dictionary<string, Channel> existingChannels,
        int id)
    {
        _logger.LogInformation("Processing {SubscriptionCount} subscriptions for add/update operations", ingestedSubscriptions.Count);

        foreach (SubscriptionUpdateYamlData subscription in ingestedSubscriptions)
        {
            _logger.LogInformation("Processing subscription {SubscriptionId} from {SourceRepository} to {TargetRepository}/{TargetBranch} on channel {Channel}", 
                subscription.Id, subscription.SourceRepository, subscription.TargetRepository, subscription.TargetBranch, subscription.Channel);

            if (subscription.Id == Guid.Empty)
            {
                var message = $"Subscription {subscription.SourceRepository} -> {subscription.TargetRepository} / {subscription.TargetBranch} ({subscription.Channel}) has invalid or missing ID";
                _logger.LogError("Subscription validation failed: {Message}", message);
                throw new InvalidOperationException(message);
            }

            if (!existingChannels.TryGetValue(subscription.Channel, out Channel? channel))
            {
                var message = $"Channel {subscription.Channel} set for subscription {subscription.Id} does not exist";
                _logger.LogError("Subscription validation failed: {Message}", message);
                throw new InvalidOperationException(message);
            }

            if (bool.TryParse(subscription.SourceEnabled, out bool sourceEnabled))
            {
                if (sourceEnabled && string.IsNullOrEmpty(subscription.SourceDirectory) && string.IsNullOrEmpty(subscription.TargetDirectory))
                {
                    var message = "The request is invalid. Source-enabled subscriptions require the source or target directory to be set";
                    _logger.LogError("Subscription validation failed for {SubscriptionId}: {Message}", subscription.Id, message);
                    throw new InvalidOperationException(message);
                }

                if (!sourceEnabled && !string.IsNullOrEmpty(subscription.SourceDirectory))
                {
                    var message = "The request is invalid. Source directory can be set only for source-enabled subscriptions";
                    _logger.LogError("Subscription validation failed for {SubscriptionId}: {Message}", subscription.Id, message);
                    throw new InvalidOperationException(message);
                }

                if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
                {
                    var message = "The request is invalid. Only one of source or target directory can be set";
                    _logger.LogError("Subscription validation failed for {SubscriptionId}: {Message}", subscription.Id, message);
                    throw new InvalidOperationException(message);
                }

                if (sourceEnabled && bool.TryParse(subscription.Batchable, out bool batchable) && batchable)
                {
                    var message = "The request is invalid. Batched codeflow subscriptions are not supported.";
                    _logger.LogError("Subscription validation failed for {SubscriptionId}: {Message}", subscription.Id, message);
                    throw new InvalidOperationException(message);
                }
            }

            var subscriptionModel = new Subscription
            {
                Id = subscription.Id,
                SourceRepository = subscription.SourceRepository,
                TargetRepository = subscription.TargetRepository,
                TargetBranch = subscription.TargetBranch,
                Enabled = bool.TryParse(subscription.Enabled, out bool e) && e,
                SourceEnabled = bool.TryParse(subscription.SourceEnabled, out bool se) && se,
                SourceDirectory = subscription.SourceDirectory,
                TargetDirectory = subscription.TargetDirectory,
                PolicyObject = new SubscriptionPolicy
                {
                    Batchable = bool.TryParse(subscription.Batchable, out bool b) && b,
                    MergePolicies =
                    [
                        ..subscription.MergePolicies?.Select(p => new MergePolicyDefinition
                        {
                            Name = p.Name,
                            Properties = p.Properties?.ToDictionary(p => p.Key, p => JToken.FromObject(p.Value))
                        }) ?? []
                    ],
                    UpdateFrequency = Enum.Parse<UpdateFrequency>(subscription.UpdateFrequency, true),
                },
                PullRequestFailureNotificationTags = subscription.FailureNotificationTags,
                // TODO: Excluded assets need an ID
                // ExcludedAssets = subscription.ExcludedAssets == null ? [] : [.. subscription.ExcludedAssets.Select(e => new AssetFilter() {  })],
                ConfigurationSourceId = id,
            };

            // Check that we're not about add an existing subscription that is identical
            Subscription? equivalentSubscription = FindEquivalentSubscription(existingSubscriptions.Values, subscriptionModel);
            if (equivalentSubscription != null)
            {
                var message = $"The subscription '{equivalentSubscription.Id}' already performs the same update.";
                _logger.LogError("Duplicate subscription detected: {Message}", message);
                throw new InvalidOperationException(message);
            }

            // Check for codeflow subscription conflicts
            var conflictError = ValidateCodeflowSubscriptionConflicts(existingSubscriptions.Values, subscriptionModel);
            if (conflictError != null)
            {
                _logger.LogError("Codeflow subscription conflict detected for {SubscriptionId}: {ConflictError}", subscription.Id, conflictError);
                throw new InvalidOperationException(conflictError);
            }

            if (!existingSubscriptions.TryGetValue(subscription.Id, out Subscription? existingSubscription))
            {
                _logger.LogInformation("Adding new subscription {SubscriptionId}", subscription.Id);
                var ns = _context.Subscriptions.Add(subscriptionModel);
                existingSubscriptions.Add(subscription.Id, ns.Entity);
            }
            else
            {
                if (existingSubscription.TargetBranch != subscriptionModel.TargetBranch)
                {
                    var message = $"Changing the target branch of an existing subscription {subscription.Id} is not allowed";
                    _logger.LogError("Subscription update validation failed: {Message}", message);
                    throw new InvalidOperationException(message);
                }

                if (existingSubscription.TargetRepository != subscriptionModel.TargetRepository)
                {
                    var message = $"Changing the target repository of an existing subscription {subscription.Id} is not allowed";
                    _logger.LogError("Subscription update validation failed: {Message}", message);
                    throw new InvalidOperationException(message);
                }

                if (existingSubscription.PolicyObject.Batchable != subscriptionModel.PolicyObject.Batchable)
                {
                    var message = $"Changing the batchable attribute of an existing subscription {subscription.Id} is not allowed";
                    _logger.LogError("Subscription update validation failed: {Message}", message);
                    throw new InvalidOperationException(message);
                }

                existingSubscription.SourceRepository = subscriptionModel.SourceRepository;
                existingSubscription.TargetRepository = subscriptionModel.TargetRepository;
                existingSubscription.Enabled = subscriptionModel.Enabled;
                existingSubscription.SourceEnabled = subscriptionModel.SourceEnabled;
                existingSubscription.SourceDirectory = subscriptionModel.SourceDirectory;
                existingSubscription.TargetDirectory = subscriptionModel.TargetDirectory;
                existingSubscription.PolicyObject = subscriptionModel.PolicyObject;
                existingSubscription.PullRequestFailureNotificationTags = subscriptionModel.PullRequestFailureNotificationTags;
                existingSubscription.ConfigurationSourceId = subscriptionModel.ConfigurationSourceId;
                existingSubscription.ChannelId = channel.Id;
                // TODO: Excluded assets need an ID

                _context.Subscriptions.Update(existingSubscription);
            }
        }

        _logger.LogDebug("Completed processing subscriptions for add/update operations");
    }

    private void AddOrUpdateChannels(
        Dictionary<string, Channel> existingChannels,
        IReadOnlyList<ChannelYamlData> ingestedChannels,
        int sourceId)
    {
        _logger.LogInformation("Processing {ChannelCount} channels for add/update operations", ingestedChannels.Count);

        foreach (ChannelYamlData channelData in ingestedChannels)
        {
            if (!existingChannels.TryGetValue(channelData.Name, out Channel? channel))
            {
                channel = new Channel
                {
                    Name = channelData.Name,
                    Classification = channelData.Classification,
                    ConfigurationSourceId = sourceId,
                };
                _logger.LogInformation("Adding new channel {ChannelName}", channelData.Name);
                var newChannel = _context.Channels.Add(channel);
                existingChannels[channel.Name] = newChannel.Entity;
            }
            else
            {
                channel.Classification = channelData.Classification;
                _context.Channels.Update(channel);
            }
        }
    }

    private void AddOrUpdateDefaultChannels(
        List<DefaultChannel> existingDefaultChannels,
        IReadOnlyList<DefaultChannelYamlData> ingestedDefaultChannels,
        Dictionary<string, Channel> existingChannels,
        int id)
    {
        _logger.LogInformation("Processing {DefaultChannelCount} default channels for add/update operations", ingestedDefaultChannels.Count);

        foreach (DefaultChannelYamlData defaultChannelData in ingestedDefaultChannels)
        {
            if (!existingChannels.TryGetValue(defaultChannelData.Channel, out Channel? channel))
            {
                var message = $"Channel {defaultChannelData.Channel} does not exist";
                _logger.LogError("Default channel validation failed: {Message}", message);
                throw new InvalidOperationException(message);
            }
            
            DefaultChannel? defaultChannel = existingDefaultChannels
                .Where(dc => dc.Repository == defaultChannelData.Repository && dc.Branch == defaultChannelData.Branch && dc.ChannelId == channel.Id)
                .FirstOrDefault();

            if (defaultChannel == null)
            {
                defaultChannel = new DefaultChannel
                {
                    Repository = defaultChannelData.Repository,
                    Branch = defaultChannelData.Branch,
                    ChannelId = channel.Id,
                    Enabled = defaultChannelData.Enabled,
                    ConfigurationSourceId = id,
                };
                _logger.LogInformation("Adding new default channel for {Repository} / {Branch} on channel {ChannelName}", 
                    defaultChannelData.Repository, defaultChannelData.Branch, defaultChannelData.Channel);
                var newDc = _context.DefaultChannels.Add(defaultChannel);
                existingDefaultChannels.Add(newDc.Entity);
            }
            else
            {
                defaultChannel.Enabled = defaultChannelData.Enabled;
                _context.DefaultChannels.Update(defaultChannel);
            }
        }
    }

    private void RemoveSubscriptions(
        Dictionary<Guid, Subscription> existingSubscriptions,
        IReadOnlyCollection<SubscriptionUpdateYamlData> subscriptions)
    {
        if (existingSubscriptions.Count == 0)
        {
            _logger.LogDebug("No existing subscriptions to remove");
            return;
        }

        HashSet<Guid> newIds = [.. subscriptions.Select(s => s.Id)];

        if (newIds.Count != subscriptions.Count)
        {
            var message = "Duplicate subscription IDs found in configuration.";
            _logger.LogError("Subscription validation failed: {Message}", message);
            throw new InvalidOperationException(message);
        }

        HashSet<Guid> toRemove = [.. existingSubscriptions.Keys.Except(newIds)];
        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Removing {SubscriptionCount} subscriptions that are no longer in configuration: {SubscriptionIds}", 
                toRemove.Count, string.Join(", ", toRemove));
            _context.Subscriptions.RemoveRange(existingSubscriptions.Values.Where(s => toRemove.Contains(s.Id)));
        }
        else
        {
            _logger.LogDebug("No subscriptions to remove");
        }
    }

    private void RemoveChannels(
        Dictionary<string, Channel> existingChannels,
        IReadOnlyList<ChannelYamlData> channels)
    {
        if (existingChannels.Count == 0)
        {
            _logger.LogDebug("No existing channels to remove");
            return;
        }

        HashSet<string> newNames = [.. channels.Select(c => c.Name)];
        if (newNames.Count != channels.Count)
        {
            var message = "Duplicate channel names found in configuration.";
            _logger.LogError("Channel validation failed: {Message}", message);
            throw new InvalidOperationException(message);
        }

        HashSet<string> toRemove = [.. existingChannels.Keys.Except(newNames)];
        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Removing {ChannelCount} channels that are no longer in configuration: {ChannelNames}", 
                toRemove.Count, string.Join(", ", toRemove));
            _context.Channels.RemoveRange(existingChannels.Values.Where(c => toRemove.Contains(c.Name)));
        }
        else
        {
            _logger.LogDebug("No channels to remove");
        }
    }

    private void RemoveDefaultChannels(
        List<DefaultChannel> existingDefaultChannels,
        IReadOnlyList<DefaultChannelYamlData> defaultChannels)
    {
        if (existingDefaultChannels.Count == 0)
        {
            _logger.LogDebug("No existing default channels to remove");
            return;
        }

        HashSet<int> toRemove =
        [
            .. existingDefaultChannels
                .Where(d => !defaultChannels.Any(c => c.Repository == d.Repository && c.Branch == d.Branch && c.Channel == d.Channel.Name))
                .Select(d => d.Id)
        ];

        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Removing {DefaultChannelCount} default channels that are no longer in configuration", toRemove.Count);
            _context.DefaultChannels.RemoveRange(existingDefaultChannels.Where(dc => toRemove.Contains(dc.Id)));
        }
        else
        {
            _logger.LogDebug("No default channels to remove");
        }
    }

    /// <summary>
    ///     Validates codeflow subscription conflicts
    /// </summary>
    /// <param name="subscription">Subscription to validate</param>
    /// <returns>Error message if conflict found, null if no conflicts</returns>
    private static string? ValidateCodeflowSubscriptionConflicts(IReadOnlyCollection<Subscription> existingSubscriptions, Subscription subscription)
    {
        if (!subscription.SourceEnabled)
        {
            return null;
        }

        // Check for backflow conflicts (source directory not empty)
        if (!string.IsNullOrEmpty(subscription.SourceDirectory))
        {
            var conflictingBackflowSubscription = FindConflictingBackflowSubscription(existingSubscriptions, subscription);
            if (conflictingBackflowSubscription != null)
            {
                return $"A backflow subscription '{conflictingBackflowSubscription.Id}' already exists for the same target repository and branch. " +
                       "Only one backflow subscription is allowed per target repository and branch combination.";
            }
        }

        // Check for forward flow conflicts (target directory not empty)
        if (!string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            var conflictingForwardFlowSubscription = FindConflictingForwardFlowSubscription(existingSubscriptions, subscription);
            if (conflictingForwardFlowSubscription != null)
            {
                return $"A forward flow subscription '{conflictingForwardFlowSubscription.Id}' already exists for the same VMR repository, branch, and target directory. " +
                       "Only one forward flow subscription is allowed per VMR repository, branch, and target directory combination.";
            }
        }

        return null;
    }

    /// <summary>
    ///     Find an existing subscription in the database with the same key data as the subscription we are adding/updating
    ///     
    ///     This should be called before updating or adding new subscriptions to the database
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Subscription if it is found, null otherwise</returns>
    private static Subscription? FindEquivalentSubscription(IReadOnlyCollection<Subscription> existingSubscriptions, Subscription updatedOrNewSubscription) =>
        // Compare subscriptions based on key elements and a different id
        existingSubscriptions.FirstOrDefault(sub =>
            sub.SourceRepository == updatedOrNewSubscription.SourceRepository
                && sub.ChannelId == updatedOrNewSubscription.Channel.Id
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.SourceEnabled == updatedOrNewSubscription.SourceEnabled
                && sub.SourceDirectory == updatedOrNewSubscription.SourceDirectory
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);

    /// <summary>
    ///     Find a conflicting backflow subscription (different subscription targeting same repo/branch)
    /// </summary>
    /// <param name="updatedOrNewSubscription">Subscription model with updated data.</param>
    /// <returns>Conflicting subscription if found, null otherwise</returns>
    private static Subscription? FindConflictingBackflowSubscription(IReadOnlyCollection<Subscription> existingSubscriptions, Subscription updatedOrNewSubscription) =>
        existingSubscriptions.FirstOrDefault(sub =>
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
    private static Subscription? FindConflictingForwardFlowSubscription(IReadOnlyCollection<Subscription> existingSubscriptions, Subscription updatedOrNewSubscription) =>
        existingSubscriptions.FirstOrDefault(sub =>
            sub.SourceEnabled == true
                && !string.IsNullOrEmpty(sub.TargetDirectory) // Forward flow subscription
                && sub.TargetRepository == updatedOrNewSubscription.TargetRepository
                && sub.TargetBranch == updatedOrNewSubscription.TargetBranch
                && sub.TargetDirectory == updatedOrNewSubscription.TargetDirectory
                && sub.Id != updatedOrNewSubscription.Id);
}
