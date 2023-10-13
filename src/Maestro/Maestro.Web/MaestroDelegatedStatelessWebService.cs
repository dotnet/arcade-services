// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Fabric;
using System;
using Microsoft.ServiceFabric.Services.Runtime;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using System.Collections.Generic;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using System.IO;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Hosting;

namespace Maestro.Web;

public class MaestroDelegatedStatelessWebService<TStartup> : StatelessService where TStartup : class
{
    private readonly Action<IWebHostBuilder> _configureHost;
    private readonly Action<IServiceCollection> _configureServices;

    public MaestroDelegatedStatelessWebService(
        StatelessServiceContext context,
        Action<IWebHostBuilder> configureHost,
        Action<IServiceCollection> configureServices) : base(context)
    {
        _configureHost = configureHost;
        _configureServices = configureServices;
    }

    private ServiceInstanceListener CreateServiceInstanceListener(string endpointName)
        => new ServiceInstanceListener(
                context =>
                {
                    return new HttpSysCommunicationListener(
                        context,
                        "ServiceEndpoint",
                        (url, listener) =>
                        {
                            var builder = new WebHostBuilder()
                                .UseHttpSys()
                                .UseContentRoot(Directory.GetCurrentDirectory())
                                .UseSetting(WebHostDefaults.ApplicationKey,
                                    typeof(TStartup).Assembly.GetName().Name);
                            _configureHost(builder);

                            return builder.ConfigureServices(
                                    services =>
                                    {
                                        services.AddSingleton<ServiceContext>(context);
                                        services.AddSingleton(context);
                                        services.AddSingleton<IServiceLoadReporter>(new StatelessServiceLoadReporter(Partition));
                                        services.AddSingleton<IStartup>(
                                            provider =>
                                            {
                                                var env = provider.GetRequiredService<IHostEnvironment>();
                                                return new DelegatedStatelessWebServiceStartup<TStartup>(
                                                    provider,
                                                    env,
                                                    _configureServices);
                                            });
                                    })
                                .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                .UseUrls(url)
                                .Build();
                        });
                });

    protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
    {
        return new[]
        {
            CreateServiceInstanceListener("ServiceEndpoint"),
            CreateServiceInstanceListener("ServiceEndpointHttp")
        };
    }
}
