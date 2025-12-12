// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;

namespace Maestro.DataProviders.ConfigurationIngestion;

public record ConfigurationDataUpdate(
    EntityChanges<IngestedSubscription> Subscriptions,
    EntityChanges<IngestedChannel> Channels,
    EntityChanges<IngestedDefaultChannel> DefaultChannels,
    EntityChanges<IngestedBranchMergePolicies> RepositoryBranches);

public record EntityChanges<T>(
    IEnumerable<T> Creations,
    IEnumerable<T> Updates,
    IEnumerable<T> Removals) where T: class;
