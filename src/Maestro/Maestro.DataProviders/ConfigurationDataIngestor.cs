// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.Darc.Yaml;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

#nullable enable
namespace Maestro.DataProviders;

public interface IConfigurationDataIngestor
{
    Task IngestConfiguration(string repoUri, string branch);
}

public class ConfigurationDataIngestor : IConfigurationDataIngestor
{
    private readonly BuildAssetRegistryContext _context;
    private readonly IGitRepoFactory _gitRepoFactory;

    public ConfigurationDataIngestor(
        BuildAssetRegistryContext context,
        IGitRepoFactory gitRepoFactory)
    {
        _context = context;
        _gitRepoFactory = gitRepoFactory;
    }

    public async Task IngestConfiguration(string repoUri, string branch)
    {
        IGitRepo repo = _gitRepoFactory.CreateClient(repoUri);
        List<string> subscriptionFiles = []; // TODO repo.GetFilesAsync(repoUri, branch, "subscriptions");
        List<string> channelFiles = []; // TODO repo.GetFilesAsync(repoUri, branch, "channels");
        List<string> defaultChannelFiles = []; // TODO repo.GetFilesAsync(repoUri, branch, "default-channels");

        IDeserializer serializer = new DeserializerBuilder().Build();

        IReadOnlyCollection<SubscriptionUpdateYamlData> ingestedSubscriptions = [.. subscriptionFiles.SelectMany(serializer.Deserialize<List<SubscriptionUpdateYamlData>>)];
        IReadOnlyList<ChannelYamlData> ingestedChannels = [.. channelFiles.SelectMany(serializer.Deserialize<List<ChannelYamlData>>)];
        IReadOnlyList<DefaultChannelYamlData> ingestedDefaultChannels = [.. defaultChannelFiles.SelectMany(serializer.Deserialize<List<DefaultChannelYamlData>>)];

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var configurationSource = await _context.ConfigurationSources
                .Where(cs => cs.Uri == repoUri && cs.Branch == branch)
                .FirstOrDefaultAsync();

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

            // Remove any subscriptions, channels, or default channels that are no longer present in the configuration
            RemoveSubscriptions(existingSubscriptions, ingestedSubscriptions);
            RemoveDefaultChannels(existingDefaultChannels, ingestedDefaultChannels);
            RemoveChannels(existingChannels, ingestedChannels);

            // Add or update any items
            AddOrUpdateChannels(existingChannels, ingestedChannels, configurationSource.Id);
            AddOrUpdateDefaultChannels(existingDefaultChannels, ingestedDefaultChannels, existingChannels, configurationSource.Id);
            AddOrUpdateSubscriptions(existingSubscriptions, ingestedSubscriptions, existingChannels, configurationSource.Id);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            // TODO: Handle failure
        }
    }

    private void AddOrUpdateSubscriptions(
        Dictionary<Guid, Subscription> existingSubscriptions,
        IReadOnlyCollection<SubscriptionUpdateYamlData> ingestedSubscriptions,
        Dictionary<string, Channel> existingChannels,
        int id)
    {
        foreach (SubscriptionUpdateYamlData subscription in ingestedSubscriptions)
        {
            if (subscription.Id == Guid.Empty)
            {
                throw new InvalidOperationException($"Subscription {subscription.SourceRepository} -> {subscription.TargetRepository} / {subscription.TargetBranch} ({subscription.Channel}) has invalid or missing ID");
            }

            if (!existingChannels.TryGetValue(subscription.Channel, out Channel? channel))
            {
                throw new InvalidOperationException($"Channel {subscription.Channel} set for subscription {subscription.Id} does not exist");
            }

            if (bool.TryParse(subscription.SourceEnabled, out bool sourceEnabled))
            {
                if (sourceEnabled && string.IsNullOrEmpty(subscription.SourceDirectory) && string.IsNullOrEmpty(subscription.TargetDirectory))
                {
                    throw new InvalidOperationException("The request is invalid. Source-enabled subscriptions require the source or target directory to be set");
                }

                if (!sourceEnabled && !string.IsNullOrEmpty(subscription.SourceDirectory))
                {
                    throw new InvalidOperationException("The request is invalid. Source directory can be set only for source-enabled subscriptions");
                }

                if (!string.IsNullOrEmpty(subscription.SourceDirectory) && !string.IsNullOrEmpty(subscription.TargetDirectory))
                {
                    throw new InvalidOperationException("The request is invalid. Only one of source or target directory can be set");
                }

                if (sourceEnabled && bool.TryParse(subscription.Batchable, out bool batchable) && batchable)
                {
                    throw new InvalidOperationException("The request is invalid. Batched codeflow subscriptions are not supported.");
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
                throw new InvalidOperationException($"The subscription '{equivalentSubscription.Id}' already performs the same update.");
            }

            // Check for codeflow subscription conflicts
            var conflictError = ValidateCodeflowSubscriptionConflicts(existingSubscriptions.Values, subscriptionModel);
            if (conflictError != null)
            {
                throw new InvalidOperationException(conflictError);
            }

            if (!existingSubscriptions.TryGetValue(subscription.Id, out Subscription? existingSubscription))
            {
                var ns = _context.Subscriptions.Add(subscriptionModel);
                existingSubscriptions[subscription.Id] = ns.Entity;
            }
            else
            {
                existingSubscription.SourceRepository = subscriptionModel.SourceRepository;
                existingSubscription.TargetRepository = subscriptionModel.TargetRepository;
                existingSubscription.TargetBranch = subscriptionModel.TargetBranch;
                existingSubscription.Enabled = subscriptionModel.Enabled;
                existingSubscription.SourceEnabled = subscriptionModel.SourceEnabled;
                existingSubscription.SourceDirectory = subscriptionModel.SourceDirectory;
                existingSubscription.TargetDirectory = subscriptionModel.TargetDirectory;
                existingSubscription.PolicyObject = subscriptionModel.PolicyObject;
                existingSubscription.PullRequestFailureNotificationTags = subscriptionModel.PullRequestFailureNotificationTags;
                existingSubscription.ConfigurationSourceId = subscriptionModel.ConfigurationSourceId;
                _context.Subscriptions.Update(existingSubscription);
            }
        }
    }

    private void AddOrUpdateChannels(
        Dictionary<string, Channel> existingChannels,
        IReadOnlyList<ChannelYamlData> ingestedChannels,
        int sourceId)
    {
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
        foreach (DefaultChannelYamlData defaultChannelData in ingestedDefaultChannels)
        {
            if (!existingChannels.TryGetValue(defaultChannelData.Channel, out Channel? channel))
            {
                throw new InvalidOperationException($"Channel {defaultChannelData.Channel} does not exist");
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
            return;
        }

        HashSet<Guid> newIds = [.. subscriptions.Select(s => s.Id)];

        if (newIds.Count != subscriptions.Count)
        {
            throw new InvalidOperationException("Duplicate subscription IDs found in configuration.");
        }

        HashSet<Guid> toRemove = [.. existingSubscriptions.Keys.Except(newIds)];
        _context.Subscriptions.RemoveRange(existingSubscriptions.Values.Where(s => toRemove.Contains(s.Id)));
    }

    private void RemoveChannels(
        Dictionary<string, Channel> existingChannels,
        IReadOnlyList<ChannelYamlData> channels)
    {
        if (existingChannels.Count == 0)
        {
            return;
        }

        HashSet<string> newNames = [.. channels.Select(c => c.Name)];
        if (newNames.Count != channels.Count)
        {
            throw new InvalidOperationException("Duplicate channel names found in configuration.");
        }

        HashSet<string> toRemove = [.. existingChannels.Keys.Except(newNames)];
        _context.Channels.RemoveRange(existingChannels.Values.Where(c => toRemove.Contains(c.Name)));
    }

    private void RemoveDefaultChannels(
        List<DefaultChannel> existingDefaultChannels,
        IReadOnlyList<DefaultChannelYamlData> defaultChannels)
    {
        if (existingDefaultChannels.Count == 0)
        {
            return;
        }

        HashSet<int> toRemove =
        [
            .. existingDefaultChannels
                .Where(d => !defaultChannels.Any(c => c.Repository == d.Repository && c.Branch == d.Branch && c.Channel == d.Channel.Name))
                .Select(d => d.Id)
        ];
        _context.DefaultChannels.RemoveRange(existingDefaultChannels.Where(dc => toRemove.Contains(dc.Id)));
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
