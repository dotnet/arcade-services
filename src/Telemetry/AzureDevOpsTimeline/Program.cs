// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
                                o.KustoQueryConnectionString = c["KustoQueryConnectionString"];
                                o.KustoIngestConnectionString = c["KustoIngestConnectionString"];
                                o.KustoDatabase = c["KustoDatabase"];
                                o.ParallelRequests = c["ParallelRequests"];
                                o.InitialDelay = c["InitialDelay"];
                                o.Interval = c["Interval"];
                                o.BuildBatchSize = c["BuildBatchSize"];
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

                            services.AddSingleton<ITimelineTelemetryRepository>(p =>
                            {
                                IConfiguration c = p.GetRequiredService<IConfiguration>();

                                return new KustoTimelineTelemetryRepository(
                                    logger: p.GetService<ILogger<KustoTimelineTelemetryRepository>>(),
                                    queryConnectionString: c["KustoQueryConnectionString"],
                                    ingestConnectionString: c["KustoIngestConnectionString"],
                                    database: c["KustoDatabase"]
                                );
                            });
                        });
                    
                });
        }
    }
}
