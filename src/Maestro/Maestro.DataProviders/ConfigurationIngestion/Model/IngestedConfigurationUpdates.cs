// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

internal record IngestedConfigurationUpdates(
    EntityChanges<IngestedSubscription> Subscriptions,
    EntityChanges<IngestedChannel> Channels,
    EntityChanges<IngestedDefaultChannel> DefaultChannels,
    EntityChanges<IngestedBranchMergePolicies> RepositoryBranches)
{
    public ConfigurationUpdates ToYamls()
        => new(
            new([.. Subscriptions.Creations.Select(s => s._values)],
                [.. Subscriptions.Updates.Select(s => s._values)],
                [.. Subscriptions.Removals.Select(s => s._values)]),
            new([.. Channels.Creations.Select(c => c._values)],
                [.. Channels.Updates.Select(c => c._values)],
                [.. Channels.Removals.Select(c => c._values)]),
            new([.. DefaultChannels.Creations.Select(dc => dc._values)],
                [.. DefaultChannels.Updates.Select(dc => dc._values)],
                [.. DefaultChannels.Removals.Select(dc => dc._values)]),
            new([.. RepositoryBranches.Creations.Select(rb => rb._values)],
                [.. RepositoryBranches.Updates.Select(rb => rb._values)],
                [.. RepositoryBranches.Removals.Select(rb => rb._values)]));
}

public record EntityChanges<T>(
    IReadOnlyCollection<T> Creations,
    IReadOnlyCollection<T> Updates,
    IReadOnlyCollection<T> Removals) where T: class;
