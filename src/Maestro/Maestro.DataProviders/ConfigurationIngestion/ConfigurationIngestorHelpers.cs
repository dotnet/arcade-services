// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

internal partial class ConfigurationIngestor
{
    private static IngestedConfigurationData CreateConfigurationDataObject(Namespace namespaceEntity)
    {
        var convertedSubscriptions = namespaceEntity.Subscriptions
            .Select(sub => SqlBarClient.ToClientModelSubscription(sub))
            .Select(SubscriptionYaml.FromClientModel)
            .Select(yamlSub => new IngestedSubscription(yamlSub))
            .ToList();

        var convertedChannels = namespaceEntity.Channels
            .Select(channel => SqlBarClient.ToClientModelChannel(channel))
            .Select(ChannelYaml.FromClientModel)
            .Select(yamlChannel => new IngestedChannel(yamlChannel))
            .ToList();

        var convertedDefaultChannels = namespaceEntity.DefaultChannels
            .Select(dc => SqlBarClient.ToClientModelDefaultChannel(dc))
            .Select(DefaultChannelYaml.FromClientModel)
            .Select(yamlDc => new IngestedDefaultChannel(yamlDc))
            .ToList();

        var convertedBranchMergePolicies = namespaceEntity.RepositoryBranches
            .Select(rb => SqlBarClient.ToClientModelRepositoryBranch(rb))
            .Select(BranchMergePoliciesYaml.FromClientModel)
            .Select(rbYaml => new IngestedBranchMergePolicies(rbYaml))
            .ToList();

        return new IngestedConfigurationData(
            convertedSubscriptions,
            convertedChannels,
            convertedDefaultChannels,
            convertedBranchMergePolicies);
    }

    private static IngestedConfigurationUpdates ComputeEntityUpdates(
        IngestedConfigurationData configurationData,
        IngestedConfigurationData existingConfigurationData)
    {
        EntityChanges<IngestedSubscription> subscriptionChanges =
            ComputeUpdatesForEntity<IngestedSubscription, Guid>(
                existingConfigurationData.Subscriptions,
                configurationData.Subscriptions);

        EntityChanges<IngestedChannel> channelChanges =
            ComputeUpdatesForEntity<IngestedChannel, string>(
                existingConfigurationData.Channels,
                configurationData.Channels);

        EntityChanges<IngestedDefaultChannel> defaultChannelChanges =
            ComputeUpdatesForEntity<IngestedDefaultChannel, (string, string, string)>(
                existingConfigurationData.DefaultChannels,
                configurationData.DefaultChannels);

        EntityChanges<IngestedBranchMergePolicies> branchMergePolicyChanges =
            ComputeUpdatesForEntity<IngestedBranchMergePolicies, (string, string)>(
                existingConfigurationData.BranchMergePolicies,
                configurationData.BranchMergePolicies);

        return new IngestedConfigurationUpdates(
            subscriptionChanges,
            channelChanges,
            defaultChannelChanges,
            branchMergePolicyChanges);
    }

    private static EntityChanges<T> ComputeUpdatesForEntity<T, TId>(
        IReadOnlyCollection<T> dbEntities,
        IReadOnlyCollection<T> externalEntities)
        where T : IExternallySyncedEntity<TId>
        where TId : notnull
    {
        var dbIds = dbEntities.Select(e => e.UniqueId).ToHashSet();
        var externalIds = externalEntities.Select(e => e.UniqueId).ToHashSet();

        IReadOnlyCollection<T> creations = [.. externalEntities
            .Where(e => !dbIds.Contains(e.UniqueId))];

        IReadOnlyCollection<T> removals = [.. dbEntities
            .Where(e => !externalIds.Contains(e.UniqueId))];

        IReadOnlyCollection<T> updates = [.. externalEntities
            .Where(e => dbIds.Contains(e.UniqueId))];

        return new EntityChanges<T>(creations, updates, removals);
    }

    private static Subscription ConvertIngestedSubscriptionToDao(
        IngestedSubscription subscription,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        if (!existingChannelsByName.TryGetValue(subscription.Values.Channel, out Channel? existingChannel))
        {
            throw new InvalidOperationException(
                $"Channel '{subscription.Values.Channel}' not found for subscription creation.");
        }

        return new Subscription
        {
            Id = subscription.Values.Id,
            ChannelId = existingChannel.Id,
            Channel = existingChannel,
            SourceRepository = subscription.Values.SourceRepository,
            TargetRepository = subscription.Values.TargetRepository,
            TargetBranch = subscription.Values.TargetBranch,
            PolicyObject = new SubscriptionPolicy
            {
                UpdateFrequency = (UpdateFrequency)(int)subscription.Values.UpdateFrequency,
                Batchable = subscription.Values.Batchable,
                MergePolicies = [.. subscription.Values.MergePolicies.Select(ConvertMergePolicyYamlToDao)],
            },
            Enabled = subscription.Values.Enabled,
            SourceEnabled = subscription.Values.SourceEnabled,
            SourceDirectory = subscription.Values.SourceDirectory,
            TargetDirectory = subscription.Values.TargetDirectory,
            PullRequestFailureNotificationTags = subscription.Values.FailureNotificationTags,
            ExcludedAssets = subscription.Values.ExcludedAssets == null ? [] : [.. subscription.Values.ExcludedAssets.Select(asset => new AssetFilter() { Filter = asset })],
            Namespace = namespaceEntity,
        };
    }

    private static Channel ConvertIngestedChannelToDao(
        IngestedChannel channel,
        Namespace namespaceEntity)
        =>  new()
        {
            Name = channel.Values.Name,
            Classification = channel.Values.Classification,
            Namespace = namespaceEntity,
        };

    private static DefaultChannel ConvertIngestedDefaultChannelToDao(
        IngestedDefaultChannel defaultChannel,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        if (existingChannelsByName.TryGetValue(defaultChannel.Values.Channel, out Channel? existingChannel))
        {
            return new DefaultChannel
            {
                ChannelId = existingChannel.Id,
                Channel = existingChannel,
                Repository = defaultChannel.Values.Repository,
                Namespace = namespaceEntity,
                Branch = defaultChannel.Values.Branch,
                Enabled = defaultChannel.Values.Enabled,
            };
        }
        else
        {
            throw new InvalidOperationException(
                $"Channel '{defaultChannel.Values.Channel}' not found for default channel creation.");
        }
    }

    private static RepositoryBranch ConvertIngestedBranchMergePoliciesToDao(
        IngestedBranchMergePolicies branchMergePolicies,
        Namespace namespaceEntity)
    {
        var policyObject = new RepositoryBranch.Policy
        {
            MergePolicies = [.. branchMergePolicies.Values.MergePolicies.Select(ConvertMergePolicyYamlToDao)],
        };

        var branchMergePolicyDao = new RepositoryBranch
        {
            RepositoryName = branchMergePolicies.Values.Repository,
            BranchName = branchMergePolicies.Values.Branch,
            PolicyString = JsonConvert.SerializeObject(policyObject),
            Namespace = namespaceEntity,
        };

        return branchMergePolicyDao;
    }

    private static MergePolicyDefinition ConvertMergePolicyYamlToDao(MergePolicyYaml mergePolicy)
        => new()
        {
            Name = mergePolicy.Name,
            Properties = mergePolicy.Properties?.ToDictionary(
                p => p.Key,
                p => JToken.FromObject(p.Value)), // todo: this seems fragile. Can we change MergePolicyYaml to be <string, JToken> like the DAO & DTO?
        };
}

public class CaseInsensitiveTupleComparer<T> : IEqualityComparer<T>
{
    private readonly Func<T, string[]> _getParts;

    public CaseInsensitiveTupleComparer(Func<T, string[]> getParts)
    {
        _getParts = getParts;
    }

    public bool Equals(T? x, T? y)
    {
        if (x == null && y == null)
        {
            return true;
        }

        if (x == null || y == null)
        {
            return false;
        }

        var a = _getParts(x);
        var b = _getParts(y);

        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }

    public int GetHashCode(T obj)
    {
        var parts = _getParts(obj);

        var hash = new HashCode();
        foreach (var part in parts)
        {
            hash.Add(part, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }
}

public static class CaseInsensitiveTupleComparer
{
    public static IEqualityComparer<(string, string)> Pair() =>
        new CaseInsensitiveTupleComparer<(string, string)>(t => [t.Item1, t.Item2]);

    public static IEqualityComparer<(string, string, string)> Triple() =>
        new CaseInsensitiveTupleComparer<(string, string, string)>(t => [t.Item1, t.Item2, t.Item3]);
}
