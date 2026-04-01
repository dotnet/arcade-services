// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Model;

public abstract class IExternallySyncedEntity
{
    /// <summary>
    /// Returns a string representation of the entity that includes identifying information.
    /// </summary>
    public abstract override string ToString();
}

/// <summary>
/// Defines an entity that is ingested from an external source and synchronized into the system.
/// Requires a unique identifier to match existing entities with ingested ones.
/// </summary>
/// <typeparam name="TId">The type of the unique identifier for the entity. Must be a non-nullable type.</typeparam>
public abstract class IExternallySyncedEntity<TId> : IExternallySyncedEntity
    where TId : notnull
{
    /// <summary>
    /// Gets the unique identifier for the entity.
    /// </summary>
    public abstract TId UniqueId { get; }

    /// <summary>
    /// Gets the comparer used to determine equality of the unique identifier.
    /// </summary>
    public abstract IEqualityComparer<TId> UniqueKeyComparer { get; }
}
