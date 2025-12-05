// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor;

public interface IConfigurationIngestor
{
    /// <summary>
    ///  Ingests a configuration on a given namespace, validating and persisting its data.
    /// </summary>
    /// <returns>A record of the entity changes applied during ingestion.</returns>
    Task<ConfigurationDataUpdate> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace);
}
