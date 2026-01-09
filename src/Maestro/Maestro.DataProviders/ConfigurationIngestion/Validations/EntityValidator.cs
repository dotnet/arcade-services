// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Model;

namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal class EntityValidator
{
    internal static void ValidateEntityUniqueness<T>(IEnumerable<IExternallySyncedEntity<T>> entities) where T : notnull
    {
        if (!entities.Any())
        {
            return;
        }

        // Find duplicates by grouping entities by their unique ID
        var duplicates = entities
            .GroupBy(e => e.UniqueId)
            .Where(g => g.Count() > 1)
            .Select(g => g.First())
            .ToList();

        if (duplicates.Any())
        {
            var duplicateInfo = string.Join(", ", duplicates.Select(e => e.ToString()));
            var entityTypeName = entities.First().GetType().Name;

            throw new IngestionEntityValidationException(
                $"{entityTypeName} collection contains duplicate Ids: {duplicateInfo}");
        }
    }
}
