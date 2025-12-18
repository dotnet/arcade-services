// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Helpers;

internal record IngestedConfigurationUpdates(
    EntityChanges<IngestedSubscription> Subscriptions,
    EntityChanges<IngestedChannel> Channels,
    EntityChanges<IngestedDefaultChannel> DefaultChannels,
    EntityChanges<IngestedBranchMergePolicies> RepositoryBranches)
{
    public ConfigurationUpdates ToYamls()
        => new(
            new([.. Subscriptions.Creations.Select(s => s.Values)],
                [.. Subscriptions.Updates.Select(s => s.Values)],
                [.. Subscriptions.Removals.Select(s => s.Values)]),
            new([.. Channels.Creations.Select(c => c.Values)],
                [.. Channels.Updates.Select(c => c.Values)],
                [.. Channels.Removals.Select(c => c.Values)]),
            new([.. DefaultChannels.Creations.Select(dc => dc.Values)],
                [.. DefaultChannels.Updates.Select(dc => dc.Values)],
                [.. DefaultChannels.Removals.Select(dc => dc.Values)]),
            new([.. RepositoryBranches.Creations.Select(rb => rb.Values)],
                [.. RepositoryBranches.Updates.Select(rb => rb.Values)],
                [.. RepositoryBranches.Removals.Select(rb => rb.Values)]));
}

public record EntityChanges<T>(
    IReadOnlyCollection<T> Creations,
    IReadOnlyCollection<T> Updates,
    IReadOnlyCollection<T> Removals) where T: class;
