// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Helpers;

internal record IngestedConfigurationData(
    IReadOnlyCollection<IngestedSubscription> Subscriptions,
    IReadOnlyCollection<IngestedChannel> Channels,
    IReadOnlyCollection<IngestedDefaultChannel> DefaultChannels,
    IReadOnlyCollection<IngestedBranchMergePolicies> BranchMergePolicies)
{
    public static IngestedConfigurationData FromYamls(ConfigurationData yamlData)
        => new(
            [..yamlData.Subscriptions.Select(s => new IngestedSubscription(s))],
            [..yamlData.Channels.Select(c => new IngestedChannel(c))],
            [..yamlData.DefaultChannels.Select(dc => new IngestedDefaultChannel(dc))],
            [..yamlData.BranchMergePolicies.Select(p => new IngestedBranchMergePolicies(p))]);
}
