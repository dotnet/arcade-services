// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.Hosting;

InMemoryChannel telemetryChannel = new();

try
{
    var builder = Host.CreateApplicationBuilder();

    
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}
