﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.FeedCleaner;

InMemoryChannel telemetryChannel = new();
try
{
    var builder = Host.CreateApplicationBuilder();
    builder.ConfigureFeedCleaner(telemetryChannel);

    // We're registering BAR context as a scoped service, so we have to create a scope to resolve it
    var applicationScope = builder.Build().Services.CreateScope();

    var cleaner = applicationScope.ServiceProvider.GetRequiredService<FeedCleanerJob>();
    await cleaner.CleanManagedFeedsAsync();
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}
