// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.AzureStorage.Cache;

public static class BlobStorageCacheConfiguration
{
    public static IServiceCollection AddBlobStorageCaching(
        this IServiceCollection services,
        IConfigurationSection configuration)
    {
        services.TryAddSingleton<IBlobClientFactory, BlobClientFactory>();
        services.TryAddScoped<IContextualStorage, BlobContextualStorage>();
        services.TryAddScoped<IDistributedLockService, BlobContextualStorage>();

        services.Configure<BlobStorageSettings>(configuration);

        return services;
    }
}
