// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.QueueInsights.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildInsights.QueueInsights;

public static class QueueInsightsConfiguration
{
    public static IServiceCollection AddQueueInsights(this IServiceCollection services)
    {
        services.AddSingleton<IQueueInsightsMarkdownGenerator, QueueInsightsMarkdownGenerator>();
        services.TryAddScoped<IQueueInsightsService, QueueInsightsService>();
        services.TryAddScoped<IQueueTimeService, QueueTimeService>();
        services.TryAddScoped<IMatrixOfTruthService, MatrixOfTruthService>();

        // TODO
        services.Configure<QueueInsightsBetaSettings>("QueueInsightsBeta", (o, c) => c.Bind(o));
        services.Configure<MatrixOfTruthSettings>("MatrixOfTruth", (o, c) => c.Bind(o));

        return services;
    }
}
