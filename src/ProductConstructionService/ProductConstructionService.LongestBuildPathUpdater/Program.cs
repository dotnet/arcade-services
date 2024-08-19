// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.LongestBuildPathUpdater;

InMemoryChannel telemetryChannel = new();

try
{
    var builder = Host.CreateApplicationBuilder();

    var serviceProvider = builder.Services.BuildServiceProvider();

    serviceProvider.GetRequiredService<LongestBuildPathUpdater>().UpdateLongestBuildPathAsync();
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}
