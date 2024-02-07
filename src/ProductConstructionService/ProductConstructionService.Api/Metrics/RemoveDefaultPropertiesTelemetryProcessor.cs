// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace ProductConstructionService.Api.Metrics;

public class RemoveDefaultPropertiesTelemetryProcessor : ITelemetryProcessor
{
    private ITelemetryProcessor Next { get; set; }

    public RemoveDefaultPropertiesTelemetryProcessor(ITelemetryProcessor next)
    {
        Next = next;
    }

    public void Process(ITelemetry item)
    {
        ISupportProperties supportProperties = (ISupportProperties)item;
        supportProperties.Properties.Remove("DeveloperMode");
        supportProperties.Properties.Remove("AspNetCoreEnvironment");
        Next.Process(item);
    }
}
