using System;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.Internal.Health;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CoreHealthMonitor;

public static class Program
{
    /// <summary>
    ///     This is the entry point of the service host process.
    /// </summary>
    private static void Main()
    {
        ServiceHost.Run(
            host =>
            {
                host.RegisterStatelessService<CoreHealthMonitorService>("CoreHealthMonitorType");
                host.ConfigureServices(Configure);
            });
    }

    public static void Configure(IServiceCollection services)
    {
        services.AddDefaultJsonConfiguration();
        services.Configure<DriveMonitorOptions>("DriveMonitoring", ((o, s) => s.Bind(o)));
        services.Configure<MemoryDumpOptions>("MemoryDump", (o, s) => s.Bind(o));
    }
}
