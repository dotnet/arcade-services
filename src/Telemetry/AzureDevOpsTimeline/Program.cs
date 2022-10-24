// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Cloud.Platform.Utils;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Internal;
using System.Net.Http;
using Microsoft.DotNet.Internal.DependencyInjection;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

internal static class Program
{
    /// <summary>
    ///     This is the entry point of the service host process.
    /// </summary>
    private static void Main()
    {
        ServiceHost.Run(
            host =>
            {
                host.RegisterStatelessService<AzureDevOpsTimeline>("AzureDevOpsTimelineType");
                host.ConfigureServices(
                    services =>
                    {
                        services.AddDefaultJsonConfiguration();
                        services.Configure<AzureDevOpsTimelineOptions>("AzureDevOpsTimeline", (o, s) => s.Bind(o));

                        services.AddClientFactory<AzureDevOpsClientOptions, IAzureDevOpsClient, AzureDevOpsClient>();
                        services.Configure<AzureDevOpsClientOptions>("dnceng", "AzureDevOpsSettings:dnceng", (o, s) => s.Bind(o));
                        services.Configure<AzureDevOpsClientOptions>("dnceng-public", "AzureDevOpsSettings:dnceng-public", (o, s) => s.Bind(o));

                        services.Configure<KustoTimelineTelemetryOptions>("KustoTimelineTelemetry", (o, s) =>
                        {
                            s.Bind(o);
                        });

                        services.Configure<AzureDevOpsClientOptions>("AzureDevOpsClientOptions", (o, s) =>
                        {
                            s.Bind(o);
                        });
                            
                        services.AddSingleton<ISystemClock, SystemClock>();
                        services.AddTransient<AzureDevOpsDelegatingHandler, RetryAfterHandler>();
                        services.AddTransient<RetryAllHandler>();

                        services.Configure<HttpClientFactoryOptions>(o =>
                        {
                            o.HttpMessageHandlerBuilderActions.Add(EnableCertificateRevocationCheck);
                            // adding this handler first so it gets the response last, after other handlers have dealt with things like throttling
                            o.HttpMessageHandlerBuilderActions.Add(AddRetryAllHandler);
                            o.HttpMessageHandlerBuilderActions.Add(AddDelegatingHandlers);
                        });
                        services.AddHttpClient();
                        services.AddTransient<IAzureDevOpsClient, AzureDevOpsClient>();

                        services.AddSingleton<ITimelineTelemetryRepository, KustoTimelineTelemetryRepository>();
                        services.AddSingleton<IBuildLogScraper, BuildLogScraper>();
                        services.AddSingleton<ExponentialRetry>();
                    });                    
            });
    }

    private static void EnableCertificateRevocationCheck(HttpMessageHandlerBuilder builder)
    {
        if (builder.PrimaryHandler is HttpClientHandler httpHandler)
        {
            httpHandler.CheckCertificateRevocationList = true;
        }
    }

    private static void AddDelegatingHandlers(HttpMessageHandlerBuilder builder)
    {
        foreach(var handler in builder.Services.GetServices<AzureDevOpsDelegatingHandler>())
        {
            builder.AdditionalHandlers.Add(handler);
        }
    }

    private static void AddRetryAllHandler(HttpMessageHandlerBuilder builder)
    {
        var handler = builder.Services.GetService<RetryAllHandler>();
        builder.AdditionalHandlers.Add(handler);
    }
}
