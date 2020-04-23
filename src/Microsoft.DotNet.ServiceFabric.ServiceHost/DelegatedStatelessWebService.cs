// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

#if !NETCOREAPP3_1
using Microsoft.AspNetCore.Hosting.Internal;
using IHostEnvironment = Microsoft.Extensions.Hosting.IHostingEnvironment;
#else
using Microsoft.Extensions.Hosting;
#endif

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class DelegatedStatelessWebService<TStartup> : StatelessService where TStartup : class
    {
        private readonly Action<IWebHostBuilder> _configureHost;
        private readonly Action<IServiceCollection> _configureServices;

        public DelegatedStatelessWebService(
            StatelessServiceContext context,
            Action<IWebHostBuilder> configureHost,
            Action<IServiceCollection> configureServices) : base(context)
        {
            _configureHost = configureHost;
            _configureServices = configureServices;
        }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[]
            {
                new ServiceInstanceListener(
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
                    })
            };
        }
    }

    public class DelegatedStatelessWebServiceStartup<TStartup> : IStartup
    {
        private readonly Action<IServiceCollection> _configureServices;
        private readonly IStartup _startupImplementation;

        public DelegatedStatelessWebServiceStartup(
            IServiceProvider provider,
            IHostEnvironment env,
            Action<IServiceCollection> configureServices)
        {
            _configureServices = configureServices;
            if (typeof(IStartup).IsAssignableFrom(typeof(TStartup)))
            {
                _startupImplementation = (IStartup) ActivatorUtilities.CreateInstance<TStartup>(provider)!;
            }
            else
            {
#if !NETCOREAPP3_1
                var methods = StartupLoader.LoadMethods(provider, typeof(TStartup), env.EnvironmentName);
                _startupImplementation = new ConventionBasedStartup(methods);
#else
                throw new InvalidOperationException($"Type '{typeof(TStartup).FullName}' must implement {typeof(IStartup).FullName}");
#endif
            }
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            _configureServices(services);
            return _startupImplementation.ConfigureServices(services);
        }

        public void Configure(IApplicationBuilder app)
        {
            _startupImplementation.Configure(app);
        }
    }
}
