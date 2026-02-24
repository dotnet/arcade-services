// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Maestro.Common.Telemetry;

public class RemoveDefaultPropertiesTelemetryProcessor : ITelemetryProcessor
{
    private ITelemetryProcessor Next { get; set; }
    private const string DeveloperMode = "DeveloperMode";
    private const string AspNetCoreEnvironment = "AspNetCoreEnvironment";

    public RemoveDefaultPropertiesTelemetryProcessor(ITelemetryProcessor next)
    {
        Next = next;
    }

    public void Process(ITelemetry item)
    {
        var supportProperties = (ISupportProperties)item;
        supportProperties.Properties.Remove(DeveloperMode);
        supportProperties.Properties.Remove(AspNetCoreEnvironment);
        Next.Process(item);
    }
}
