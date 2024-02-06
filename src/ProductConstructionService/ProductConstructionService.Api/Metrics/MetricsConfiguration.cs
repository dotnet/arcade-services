// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Metrics;

public static class MetricsConfiguration
{
    public static void AddMetricsRecorders(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IMetricRecorder, MetricRecorder>();
    }
}
