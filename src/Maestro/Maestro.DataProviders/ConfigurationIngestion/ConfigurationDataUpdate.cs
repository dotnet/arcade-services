// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion;

public record ConfigurationDataUpdate(
    EntityChanges<IngestedSubscription> Subscriptions,
    EntityChanges<IngestedChannel> Channels,
    EntityChanges<IngestedDefaultChannel> DefaultChannels,
    EntityChanges<IngestedBranchMergePolicies> RepositoryBranches);

public record EntityChanges<T>(
    IReadOnlyCollection<T> Creations,
    IReadOnlyCollection<T> Updates,
    IReadOnlyCollection<T> Removals) where T: class;
