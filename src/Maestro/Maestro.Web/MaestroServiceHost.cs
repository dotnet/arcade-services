// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.Web;

public class MaestroServiceHost : ServiceHost
{
    private MaestroServiceHost() { }

    public static new void Run(Action<ServiceHost> configure)
    {
        var host = new MaestroServiceHost();
        host.InternalRun(configure);
    }

    public override ServiceHost RegisterStatelessWebService<TStartup>(string serviceTypeName, Action<IWebHostBuilder> configureWebHost = null) where TStartup : class
    {
        RegisterStatelessService(
            serviceTypeName,
            context => new MaestroDelegatedStatelessWebService<TStartup>(
                context,
                configureWebHost ?? (builder => { }),
                ApplyConfigurationToServices));
        return ConfigureServices(builder => builder.AddScoped<TStartup>());
    }
}
