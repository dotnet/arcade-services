// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor;

public interface IConfigurationIngestor
{
    /// <summary>
    /// Attempts to ingest the current configuration and indicates whether the operation was successful.
    /// </summary>
    /// <returns>true if the configuration was ingested successfully, false if it failed due to bad data </returns>
    Task<bool> TryIngestConfigurationAsync(ConfigurationData configurationData);
}
