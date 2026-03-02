// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssuesMonitor;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

var builder = Host.CreateApplicationBuilder()
    .RegisterLogging(new InMemoryChannel());
await builder.ConfigureKnownIssueMonitor();

var serviceProvider = builder.Services
    .BuildServiceProvider()
    .CreateScope()
    .ServiceProvider;

await serviceProvider.GetRequiredService<KnownIssueMonitor>().RunAsync();
