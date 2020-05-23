using System;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class ServiceHostWebSite<TStartup>
        where TStartup : class
    {
        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        [PublicAPI]
        public static void Run(string serviceTypeName)
        {
            if (ServiceFabricHelpers.RunningInServiceFabric())
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
                .ConfigureServices(ServiceHost.ConfigureDefaultServices)
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
