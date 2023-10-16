// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using JetBrains.Annotations;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.ServiceFabric.ServiceHost;

namespace Maestro.Web;

public static class MaestroServiceHostWebSite<TStartup>
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
            ServiceHostWebSite<TStartup>.Run(serviceTypeName);
        }
    }

    private static void ServiceFabricMain(string serviceTypeName)
    {
        MaestroServiceHost.Run(host => host.RegisterStatelessWebService<TStartup>(serviceTypeName,
            hostBuilder =>
                hostBuilder.ConfigureAppConfiguration((context, builder) =>                
                    builder.AddDefaultJsonConfiguration(context.HostingEnvironment, serviceProvider: null)
        )));
    }
}
