// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssuesMonitor;
using BuildInsights.ServiceDefaults;
using Maestro.Services.Common;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = await Host.CreateApplicationBuilder()
    .AddSharedConfiguration()
    .RegisterLogging(new InMemoryChannel())
    .ConfigureKnownIssueMonitor();

var serviceProvider = builder.Services
    .BuildServiceProvider()
    .CreateScope()
    .ServiceProvider;

await serviceProvider.GetRequiredService<KnownIssueMonitor>().RunAsync();
