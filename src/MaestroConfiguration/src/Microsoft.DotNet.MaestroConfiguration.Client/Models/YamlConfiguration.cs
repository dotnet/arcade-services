// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

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
            Subscriptions = SubscriptionYaml.ToPcsClientList(Subscriptions),
            Channels = ChannelYaml.ToPcsClientList(Channels),
            DefaultChannels = DefaultChannelYaml.ToPcsClientList(DefaultChannels),
            BranchMergePolicies = BranchMergePoliciesYaml.ToPcsClientList(BranchMergePolicies)
        };
    }
}
