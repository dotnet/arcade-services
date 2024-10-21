﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.AzureDevOpsTokens;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.FeedCleaner;

public static class FeedCleanerConfiguration
{
    public static void ConfigureFeedCleaner(this IHostApplicationBuilder builder, ITelemetryChannel telemetryChannel)
    {
        builder.RegisterLogging(telemetryChannel);
        builder.AddBuildAssetRegistry();

        builder.Services.Configure<FeedCleanerOptions>((options, provider) =>
        {
            builder.Configuration.GetSection("FeedCleaner").Bind(options);

            AzureDevOpsTokenProviderOptions azdoConfig = [];
            builder.Configuration.GetSection("AzureDevOps").Bind(azdoConfig);
            options.AzdoAccounts = [.. azdoConfig.Keys];
        });

        builder.Services.AddTransient<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        builder.Services.Configure<AzureDevOpsTokenProviderOptions>("AzureDevOps", (o, s) => s.Bind(o));
        builder.Services.AddTransient<IAzureDevOpsClient, AzureDevOpsClient>();
        builder.Services.AddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<FeedCleaner>>());
        builder.Services.AddTransient<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));

        builder.Services.AddTransient<FeedCleaner>();
    }
}
