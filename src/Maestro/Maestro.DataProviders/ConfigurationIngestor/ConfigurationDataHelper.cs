// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;

namespace Maestro.DataProviders.ConfigurationIngestor;

internal class ConfigurationDataHelper
{
    internal static EntityChanges<T> ComputeUpdatesForEntity<T, TId>(
    IEnumerable<T> existingEntities,
    IEnumerable<T> newEntities)
    where T : class, ExternallySyncedEntity<TId>
    where TId : notnull
    {
        var existingById = existingEntities.ToDictionary(e => e.UniqueId);
        var newById = newEntities.ToDictionary(e => e.UniqueId);

        IEnumerable<T> creations = [.. newById.Values
            .Where(e => !existingById.ContainsKey(e.UniqueId))];

        IEnumerable<T> removals = [.. existingById.Values
            .Where(e => !newById.ContainsKey(e.UniqueId))];

        IEnumerable<T> updates = [.. newById.Values
                 .Where(e => existingById.ContainsKey(e.UniqueId))];

        return new EntityChanges<T>(creations, updates, removals);
    }
}
