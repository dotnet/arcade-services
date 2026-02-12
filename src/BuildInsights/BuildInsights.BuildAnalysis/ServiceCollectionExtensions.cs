// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable once CheckNamespace

using BuildInsights.BuildAnalysis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.BuildAnalysis;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMarkdownGenerator(this IServiceCollection services)
    {
        services.TryAddSingleton<IMarkdownGenerator, MarkdownGenerator>();
        services.TryAddSingleton<HandlebarHelpers>();
        return services;
    }
}
