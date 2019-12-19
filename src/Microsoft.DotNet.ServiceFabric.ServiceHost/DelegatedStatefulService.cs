// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Castle.DynamicProxy.Internal;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class DelegatedStatefulService<TServiceImplementation> : StatefulService
        where TServiceImplementation : IServiceImplementation
    {
        private readonly Action<ContainerBuilder> _configureContainer;
        private readonly Action<IServiceCollection> _configureServices;
        private ILifetimeScope _container;

        public DelegatedStatefulService(
            StatefulServiceContext context,
            Action<IServiceCollection> configureServices,
            Action<ContainerBuilder> configureContainer) : base(context)
        {
            _configureServices = configureServices;
            _configureContainer = configureContainer;
        }

        protected override Task OnOpenAsync(ReplicaOpenMode openMode, CancellationToken cancellationToken)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ServiceContext>(Context);
            services.AddSingleton(Context);
            _configureServices(services);

            services.AddSingleton(StateManager);

            var builder = new ContainerBuilder();
            builder.Populate(services);
            _configureContainer(builder);
            _container = builder.Build();
            
            // This requires the ServiceContext up a few lines, so we can't inject it in the constructor
            _container.ResolveOptional<TemporaryFiles>()?.Initialize();

            return Task.CompletedTask;
        }

        protected override Task OnCloseAsync(CancellationToken cancellationToken)
        {
            _container?.Dispose();
            return Task.CompletedTask;
        }

        protected override void OnAbort()
        {
            _container?.Dispose();
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            Type[] ifaces = typeof(TServiceImplementation).GetAllInterfaces()
                .Where(iface => iface.IsAssignableTo<IService>())
                .ToArray();
            if (ifaces.Length == 0)
            {
                return Enumerable.Empty<ServiceReplicaListener>();
            }

            return new[]
            {
                new ServiceReplicaListener(
                    context => ServiceHostRemoting.CreateServiceRemotingListener<TServiceImplementation>(
                        context,
                        ifaces,
                        _container))
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(RunSchedule(cancellationToken),
                RunAsyncLoop(cancellationToken));
        }

        private async Task RunAsyncLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (ILifetimeScope scope = _container.BeginLifetimeScope())
                {
                    var impl = scope.Resolve<TServiceImplementation>();

                    var shouldWaitFor = await impl.RunAsync(cancellationToken);

                    if (shouldWaitFor.Equals(TimeSpan.MaxValue))
                    {
                        return;
                    }

                    await Task.Delay(shouldWaitFor, cancellationToken);
                }
            }
        }

        private async Task RunSchedule(CancellationToken cancellationToken)
        {
            await ScheduledService<TServiceImplementation>.RunScheduleAsync(_container, cancellationToken);
        }
     }
}
