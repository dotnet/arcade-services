// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.HandleBar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.BuildAnalysis;

public static class BuildAnalysisConfiguration
{
    public static IServiceCollection AddMarkdownGenerator(this IServiceCollection services)
    {
        services.TryAddScoped<IMarkdownGenerator, MarkdownGenerator>();
        services.TryAddScoped<HandlebarHelpers>();
        return services;
    }
}
