// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.DataProviders;

public static class DataProvidersExtensions
{
    public static IServiceCollection AddConfigurationIngestion(this IServiceCollection services)
    {
        services.AddTransient<ISqlBarClient, SqlBarClient>();
        services.AddTransient<IConfigurationIngestor, ConfigurationIngestor>();
        return services;
    }
}
