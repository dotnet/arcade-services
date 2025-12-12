// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib.Models.Yaml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

internal class ConfigurationDataHelper
{
    internal static ConfigurationDataUpdate ComputeEntityUpdates(
    ConfigurationData configurationData,
    ConfigurationData existingConfigurationData)
    {
        EntityChanges<SubscriptionYaml> subscriptionChanges =
            ComputeUpdatesForEntity<SubscriptionYaml, Guid>(
                existingConfigurationData.Subscriptions,
                configurationData.Subscriptions);

        EntityChanges<ChannelYaml> channelChanges =
            ComputeUpdatesForEntity<ChannelYaml, string>(
                existingConfigurationData.Channels,
                configurationData.Channels);

        EntityChanges<DefaultChannelYaml> defaultChannelChanges =
            ComputeUpdatesForEntity<DefaultChannelYaml, (string, string, string)>(
                existingConfigurationData.DefaultChannels,
                configurationData.DefaultChannels);

        EntityChanges<BranchMergePoliciesYaml> branchMergePolicyChanges =
            ComputeUpdatesForEntity<BranchMergePoliciesYaml, (string, string)>(
                existingConfigurationData.BranchMergePolicies,
                configurationData.BranchMergePolicies);

        return new ConfigurationDataUpdate(
            subscriptionChanges,
            channelChanges,
            defaultChannelChanges,
            branchMergePolicyChanges);
    }

    internal static EntityChanges<T> ComputeUpdatesForEntity<T, TId>(
    IEnumerable<T> dbEntities,
    IEnumerable<T> externalEntities)
        where T : class, IExternallySyncedEntity<TId>
        where TId : notnull
    {
        var dbEntitiesById = dbEntities.ToDictionary(e => e.UniqueId);
        var externalEntitiesById = externalEntities.ToDictionary(e => e.UniqueId);

        IEnumerable<T> creations = [.. externalEntitiesById.Values
            .Where(e => !dbEntitiesById.ContainsKey(e.UniqueId))];

        IEnumerable<T> removals = [.. dbEntitiesById.Values
            .Where(e => !externalEntitiesById.ContainsKey(e.UniqueId))];

        IEnumerable<T> updates = [.. externalEntitiesById.Values
            .Where(e => dbEntitiesById.ContainsKey(e.UniqueId))];

        return new EntityChanges<T>(creations, updates, removals);
    }

    internal static Subscription ConvertSubscriptionYamlToDao(
        SubscriptionYaml subscription,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName)
    {
        existingChannelsByName.TryGetValue(subscription.Channel, out Channel? existingChannel);

        if (existingChannel is null)
        {
            //todo find the right exception type
            throw new InvalidOperationException(
                $"Channel '{subscription.Channel}' not found for subscription creation.");
        }

        return new Subscription
        {
            Id = subscription.Id,
            ChannelId = existingChannel.Id,
            Channel = existingChannel,
            SourceRepository = subscription.SourceRepository,
            TargetRepository = subscription.TargetRepository,
            TargetBranch = subscription.TargetBranch,
            PolicyObject = new SubscriptionPolicy
            {
                UpdateFrequency = (UpdateFrequency)(int)subscription.UpdateFrequency,
                Batchable = subscription.Batchable,
                MergePolicies = [.. subscription.MergePolicies.Select(ConvertMergePolicyYamlToDao)],
            },
            Enabled = subscription.Enabled,
            SourceEnabled = subscription.SourceEnabled,
            SourceDirectory = subscription.SourceDirectory,
            TargetDirectory = subscription.TargetDirectory,
            PullRequestFailureNotificationTags = subscription.FailureNotificationTags,
            ExcludedAssets = subscription.ExcludedAssets == null ? [] : [.. subscription.ExcludedAssets.Select(asset => new AssetFilter() { Filter = asset })],
            Namespace = namespaceEntity,
        };
    }

    internal static Channel ConvertChannelYamlToDao(
        ChannelYaml channel,
        Namespace namespaceEntity)
        =>  new()
        {
            Name = channel.Name,
            Classification = channel.Classification,
            Namespace = namespaceEntity,
        };

    internal static DefaultChannel ConvertDefaultChannelYamlToDao(
        DefaultChannelYaml defaultChannel,
        Namespace namespaceEntity,
        Dictionary<string, Channel> existingChannelsByName,
        Dictionary<(string, string, string), DefaultChannelYaml>? existingDefaultChannels)
    {
        existingChannelsByName.TryGetValue(defaultChannel.Channel, out Channel? existingChannel);

        if (existingChannel is null)
        {
            //todo find the right exception type
            throw new InvalidOperationException(
                $"Channel '{defaultChannel.Channel}' not found for default channel creation.");
        }

        DefaultChannelYaml? existingDefaultChannel = null;

        existingDefaultChannels?.TryGetValue(defaultChannel.UniqueId, out existingDefaultChannel);

        var defaultChannelDao = new DefaultChannel
        {
            ChannelId = existingChannel.Id,
            Channel = existingChannel,
            Repository = defaultChannel.Repository,
            Namespace = namespaceEntity,
            Branch = defaultChannel.Branch,
            Enabled = defaultChannel.Enabled,
        };

        if (existingDefaultChannel is not null)
        {
            defaultChannelDao.Id = existingDefaultChannel.Id;
        }

        return defaultChannelDao;
    }

    internal static RepositoryBranch ConvertBranchMergePoliciesYamlToDao(
        BranchMergePoliciesYaml branchMergePolicies,
        Namespace namespaceEntity)
    {
        var policyObject = new RepositoryBranch.Policy
        {
            MergePolicies = [.. branchMergePolicies.MergePolicies.Select(ConvertMergePolicyYamlToDao)],
        };

        var branchMergePolicyDao = new RepositoryBranch
        {
            RepositoryName = branchMergePolicies.Repository,
            BranchName = branchMergePolicies.Branch,
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
