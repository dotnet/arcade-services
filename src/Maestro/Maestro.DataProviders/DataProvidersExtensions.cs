// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProductConstructionService.Common;

namespace Maestro.DataProviders;

public static class DataProvidersExtensions
{
    public static IServiceCollection AddConfigurationIngestion(this IServiceCollection services)
    {
        services.TryAddTransient<ISqlBarClient, SqlBarClient>();
        services.TryAddTransient<IConfigurationIngestor, ConfigurationIngestor>();
        services.AddSingleton<IRedisCacheFactory, RedisCacheFactory>();
        services.AddSingleton<IDistributedLock, DistributedLock>();

        return services;
    }
}
