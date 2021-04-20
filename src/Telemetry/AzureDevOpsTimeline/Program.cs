// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
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
                            services.Configure<AzureDevOpsTimelineOptions>((o, p) =>
                            {
                                var c = p.GetRequiredService<IConfiguration>();
                                o.AzureDevOpsAccessToken = c["AzureDevOpsAccessToken"];
                                o.AzureDevOpsProjects = c["AzureDevOpsProjects"];
                                o.AzureDevOpsOrganization = c["AzureDevOpsOrganization"];
                                o.AzureDevOpsUrl = c["AzureDevOpsUrl"];
                                o.ParallelRequests = c["ParallelRequests"];
                                o.InitialDelay = c["InitialDelay"];
                                o.Interval = c["Interval"];
                                o.BuildBatchSize = c["BuildBatchSize"];
                            });

                            services.Configure<KustoTimelineTelemetryOptions>((o, p) =>
                            {
                                IConfiguration config = p.GetRequiredService<IConfiguration>();
                                config.GetSection("KustoTimelineTelemetry").Bind(o);
                            });

                            services.AddSingleton<IAzureDevOpsClient>(p =>
                            {
                                IConfiguration c = p.GetRequiredService<IConfiguration>();

                                if (!int.TryParse(c["ParallelRequests"], out int parallelRequests) || parallelRequests < 1)
                                {
                                    parallelRequests = 5;
                                }

                                return new AzureDevOpsClient(
                                    baseUrl: c["AzureDevOpsUrl"],
                                    organization: c["AzureDevOpsOrganization"],
                                    maxParallelRequests: parallelRequests,
                                    accessToken: c["AzureDevOpsAccessToken"]
                                );
                            });

                            services.AddSingleton<ITimelineTelemetryRepository, KustoTimelineTelemetryRepository>();
                        });
                    
                });
        }
    }
}
