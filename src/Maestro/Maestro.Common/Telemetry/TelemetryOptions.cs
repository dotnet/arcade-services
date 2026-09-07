// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.Common.Telemetry;

public class TelemetryOptions
{
    public const string ConfigurationKey = "Telemetry";
    public const string DefaultMetricNamespace = "ProductConstructionService.Metrics";

    public string MetricNamespace { get; set; } = DefaultMetricNamespace;
}