// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssuesMonitor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();
await builder.ConfigureKnownIssueMonitor();

var serviceProvider = builder.Services
    .BuildServiceProvider()
    .CreateScope()
    .ServiceProvider;

await ActivatorUtilities.CreateInstance<KnownIssueMonitor>(serviceProvider).RunAsync();
