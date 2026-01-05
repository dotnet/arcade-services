// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;
public record YamlConfiguration(
    IReadOnlyCollection<SubscriptionYaml> Subscriptions,
    IReadOnlyCollection<ChannelYaml> Channels,
    IReadOnlyCollection<DefaultChannelYaml> DefaultChannels,
    IReadOnlyCollection<BranchMergePoliciesYaml> BranchMergePolicies)
{
    /// <summary>
    /// Converts this MaestroConfiguration YamlConfiguration to a ProductConstructionService.Client YamlConfiguration.
    /// </summary>
    public ClientYamlConfiguration ToPcsClient()
    {
        return new ClientYamlConfiguration
        {
            Subscriptions = ConvertSubscriptions(Subscriptions),
            Channels = ConvertChannels(Channels),
            DefaultChannels = ConvertDefaultChannels(DefaultChannels),
            BranchMergePolicies = ConvertBranchMergePolicies(BranchMergePolicies)
        };
    }

    private static IImmutableList<ClientSubscriptionYaml> ConvertSubscriptions(
        IReadOnlyCollection<SubscriptionYaml> subscriptions)
    {
        if (subscriptions == null || subscriptions.Count == 0)
        {
            return ImmutableList<ClientSubscriptionYaml>.Empty;
        }

        return subscriptions
            .Select(s => new ClientSubscriptionYaml(
                id: s.Id,
                enabled: s.Enabled,
                channel: s.Channel,
                sourceRepository: s.SourceRepository,
                targetRepository: s.TargetRepository,
                targetBranch: s.TargetBranch,
                updateFrequency: ConvertUpdateFrequency(s.UpdateFrequency),
                batchable: s.Batchable,
                sourceEnabled: s.SourceEnabled)
            {
                ExcludedAssets = s.ExcludedAssets?.ToImmutableList() ?? ImmutableList<string>.Empty,
                MergePolicies = ConvertMergePolicies(s.MergePolicies),
                FailureNotificationTags = s.FailureNotificationTags,
                SourceDirectory = s.SourceDirectory,
                TargetDirectory = s.TargetDirectory
            })
            .ToImmutableList();
    }

    private static IImmutableList<ClientChannelYaml> ConvertChannels(
        IReadOnlyCollection<ChannelYaml> channels)
    {
        if (channels == null || channels.Count == 0)
        {
            return ImmutableList<ClientChannelYaml>.Empty;
        }

        return channels
            .Select(c => new ClientChannelYaml(
                name: c.Name,
                classification: c.Classification))
            .ToImmutableList();
    }

    private static IImmutableList<ClientDefaultChannelYaml> ConvertDefaultChannels(
        IReadOnlyCollection<DefaultChannelYaml> defaultChannels)
    {
        if (defaultChannels == null || defaultChannels.Count == 0)
        {
            return ImmutableList<ClientDefaultChannelYaml>.Empty;
        }

        return defaultChannels
            .Select(dc => new ClientDefaultChannelYaml(
                repository: dc.Repository,
                branch: dc.Branch,
                channel: dc.Channel,
                enabled: dc.Enabled))
            .ToImmutableList();
    }

    private static IImmutableList<ClientBranchMergePoliciesYaml> ConvertBranchMergePolicies(
        IReadOnlyCollection<BranchMergePoliciesYaml> branchMergePolicies)
    {
        if (branchMergePolicies == null || branchMergePolicies.Count == 0)
        {
            return ImmutableList<ClientBranchMergePoliciesYaml>.Empty;
        }

        return branchMergePolicies
            .Select(bmp => new ClientBranchMergePoliciesYaml(
                branch: bmp.Branch,
                repository: bmp.Repository)
            {
                MergePolicies = ConvertMergePolicies(bmp.MergePolicies)
            })
            .ToImmutableList();
    }

    private static IImmutableList<ClientMergePolicyYaml> ConvertMergePolicies(
        IReadOnlyCollection<MergePolicyYaml>? mergePolicies)
    {
        if (mergePolicies == null || mergePolicies.Count == 0)
        {
            return ImmutableList<ClientMergePolicyYaml>.Empty;
        }

        return mergePolicies
            .Select(mp => new ClientMergePolicyYaml(name: mp.Name)
            {
                Properties = ConvertProperties(mp.Properties)
            })
            .ToImmutableList();
    }

    private static IImmutableDictionary<string, JToken> ConvertProperties(
        IDictionary<string, object>? properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return ImmutableDictionary<string, JToken>.Empty;
        }

        return properties
            .ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => JToken.FromObject(kvp.Value));
    }

    private static ClientUpdateFrequency ConvertUpdateFrequency(
        UpdateFrequency updateFrequency)
    {
        return updateFrequency switch
        {
            UpdateFrequency.None => ClientUpdateFrequency.None,
            UpdateFrequency.EveryDay => ClientUpdateFrequency.EveryDay,
            UpdateFrequency.EveryBuild => ClientUpdateFrequency.EveryBuild,
            UpdateFrequency.TwiceDaily => ClientUpdateFrequency.TwiceDaily,
            UpdateFrequency.EveryWeek => ClientUpdateFrequency.EveryWeek,
            UpdateFrequency.EveryTwoWeeks => ClientUpdateFrequency.EveryTwoWeeks,
            UpdateFrequency.EveryMonth => ClientUpdateFrequency.EveryMonth,
            _ => throw new ArgumentException($"Unknown UpdateFrequency value: {updateFrequency}", nameof(updateFrequency))
        };
    }
}
