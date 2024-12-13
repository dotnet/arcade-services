﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.DataProviders;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;
using Azure.Identity;
using ProductConstructionService.WorkItems;
using Maestro.Data;

namespace ProductConstructionService.SubscriptionTriggerer;

public static class SubscriptionTriggererConfiguration
{
    public static HostApplicationBuilder ConfigureSubscriptionTriggerer(
        this HostApplicationBuilder builder,
        ITelemetryChannel telemetryChannel)
    {
        DefaultAzureCredential credential = new(
            new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = builder.Configuration[ProductConstructionServiceExtension.ManagedIdentityClientId]
            });

        builder.RegisterLogging(telemetryChannel);

        builder.AddBuildAssetRegistry();
        builder.AddWorkItemProducerFactory(credential);

        builder.Services.AddTransient<DarcRemoteMemoryCache>();
        builder.Services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        builder.Services.AddTransient<IVersionDetailsParser, VersionDetailsParser>();

        builder.Services.AddTransient<SubscriptionTriggerer>();

        return builder;
    }
}
