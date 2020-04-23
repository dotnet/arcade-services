#if NETCOREAPP3_1
using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class ServiceHostWebSite<TStartup>
        where TStartup : class
    {
        private static bool RunningInServiceFabric()
        {
            string fabricApplication = Environment.GetEnvironmentVariable("Fabric_ApplicationName");
            return !string.IsNullOrEmpty(fabricApplication);
        }

        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        public static void Run(string serviceTypeName)
        {
            if (RunningInServiceFabric())
            {
                ServiceFabricMain(serviceTypeName);
            }
            else
            {
                NonServiceFabricMain();
            }
        }

        private static void NonServiceFabricMain()
        {
            new WebHostBuilder().UseKestrel()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.AddDefaultJsonConfiguration(context.HostingEnvironment);
                })
                .ConfigureLogging(
                    builder =>
                    {
                        builder.AddFilter(level => level > LogLevel.Debug);
                        builder.AddConsole();
                    })
                .UseStartup<TStartup>()
                .UseUrls("http://localhost:8080/")
                .CaptureStartupErrors(true)
                .Build()
                .Run();
        }

        private static void ServiceFabricMain(string serviceTypeName)
        {
            ServiceHost.Run(host => host.RegisterStatelessWebService<TStartup>(serviceTypeName,
                hostBuilder =>
                {
                    hostBuilder.ConfigureAppConfiguration((context, builder) =>
                    {
                        builder.AddDefaultJsonConfiguration(context.HostingEnvironment);
                    });
                }));
        }
    }
}
#endif
