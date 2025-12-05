// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Maestro.Data.Models;

namespace Maestro.DataProviders.ConfigurationIngestor;

public record ConfigurationDataUpdate(
    EntityChanges<Subscription> SubscriptionChanges,
    EntityChanges<Channel> ChannelChanges,
    EntityChanges<DefaultChannel> DefaultChannelChanges,
    EntityChanges<RepositoryBranch> RepositoryBranchChanges);


public record EntityChanges<T>(
    IEnumerable<T> EntityCreations,
    IEnumerable<T> EntityUpdates,
    IEnumerable<T> EntityRemovals) where T: class;
