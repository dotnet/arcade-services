// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.Utilities.AzureDevOps;

public static class VssConnectionExtension
{
    public static IServiceCollection AddVssConnection(this IServiceCollection services)
    {
        services.AddTransient<AzureDevOpsDelegatingHandler, RetryAfterHandler>();
        services.AddTransient<AzureDevOpsDelegatingHandler, ThrottlingHeaderLoggingHandler>();
        services.AddTransient<AzureDevOpsDelegatingHandler, LoggingHandler>();
        services.TryAddScoped<VssConnectionProvider>();
        return services;
    }
}
