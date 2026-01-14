// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Configuration;

/// <summary>
/// Wraps ConfigurationIngestor with distributed locking to ensure thread-safe configuration ingestion.
/// </summary>
public class LockedConfigurationIngestor(
    IConfigurationIngestor configurationIngestor,
    IDistributedLock distributedLock) : IConfigurationIngestor
{
    private readonly IConfigurationIngestor _configurationIngestor = configurationIngestor;
    private readonly IDistributedLock _distributedLock = distributedLock;

    public async Task<ConfigurationUpdates> IngestConfigurationAsync(
        ConfigurationData configurationData,
        string configurationNamespace,
        bool saveChanges)
    {
        return await _distributedLock.ExecuteWithLockAsync("ConfigurationIngestion", async () =>
        {
            return await _configurationIngestor.IngestConfigurationAsync(
                configurationData,
                configurationNamespace,
                saveChanges);
        });
    }
}
