// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.QueueInsights.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.QueueInsights;

public static class QueueInsightsConfiguration
{
    public static IServiceCollection AddQueueInsights(
        this IServiceCollection services,
        IConfigurationSection queueInsightsBetaConfig,
        IConfigurationSection matrixOfTruthConfig)
    {
        services.AddSingleton<IQueueInsightsMarkdownGenerator, QueueInsightsMarkdownGenerator>();
        services.TryAddScoped<IQueueInsightsService, QueueInsightsService>();
        services.TryAddScoped<IQueueTimeService, QueueTimeService>();
        services.TryAddScoped<IMatrixOfTruthService, MatrixOfTruthService>();

        services.Configure<QueueInsightsBetaSettings>(queueInsightsBetaConfig);
        services.Configure<MatrixOfTruthSettings>(matrixOfTruthConfig);

        return services;
    }
}
