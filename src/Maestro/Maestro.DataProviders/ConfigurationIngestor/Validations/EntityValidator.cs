// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Collections.Generic;
using Maestro.Data.Models;

namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

internal class EntityValidator
{
    internal static void ValidateEntityUniqueness<T>(IEnumerable<ExternallySyncedEntity<T>> entities)
    {
        var uniqueIds = entities.Select(e => e.UniqueId).ToHashSet();

        if (uniqueIds.Count != entities.Count())
        {
            throw new ArgumentException($"{typeof(T).Name} collection contains duplicate Ids.");
        }
    }
}
