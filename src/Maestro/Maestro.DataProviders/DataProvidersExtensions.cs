// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders.ConfigurationIngestion;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maestro.DataProviders;

public static class DataProvidersExtensions
{
    public static IServiceCollection AddConfigurationIngestion(this IServiceCollection services)
    {
        services.TryAddTransient<ISqlBarClient, SqlBarClient>();
        services.TryAddTransient<IGitHubTagValidator, GitHubTagValidator>();
        services.TryAddTransient<IConfigurationIngestor, ConfigurationIngestor>();
        return services;
    }
}
