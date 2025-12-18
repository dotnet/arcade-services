// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Model;

namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal class EntityValidator
{
    internal static void ValidateEntityUniqueness<T>(IEnumerable<IExternallySyncedEntity<T>> entities)
    {
        if (!entities.Any())
        {
            return;
        }

        var uniqueIds = entities.Select(e => e.UniqueId).ToHashSet();

        if (uniqueIds.Count != entities.Count())
        {
            throw new ArgumentException($"{entities.GetType().GetGenericArguments()[0].Name} collection "
            + "contains duplicate Ids.");
        }
    }
}
