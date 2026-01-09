// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

#nullable enable

namespace Maestro.DataProviders.ConfigurationIngestion.Model;

public abstract class IExternallySyncedEntity
{
    protected static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .Build();

    /// <summary>
    /// Returns a string representation of the entity that includes identifying information.
    /// </summary>
    public abstract override string ToString();

    public abstract string SerializedData { get; }
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
}
