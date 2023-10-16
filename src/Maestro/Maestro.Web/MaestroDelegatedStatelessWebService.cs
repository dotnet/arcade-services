// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Fabric;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.Runtime;

namespace Maestro.Web;

public class MaestroDelegatedStatelessWebService<TStartup> : DelegatedStatelessWebService<TStartup> where TStartup : class
{
    public MaestroDelegatedStatelessWebService(
        StatelessServiceContext context,
        Action<IWebHostBuilder> configureHost,
        Action<IServiceCollection> configureServices)
        : base(context, configureHost, configureServices)
    {
    }

    protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners() => new[]
    {
        CreateServiceInstanceListener("ServiceEndpoint"),
        CreateServiceInstanceListener("ServiceEndpointHttp")
    };
}
