// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class DelegatedStatefulService<TServiceImplementation> : StatefulService
        where TServiceImplementation : IServiceImplementation
    {
        private readonly Action<IServiceCollection> _configureServices;
        private ServiceProvider _container;

        public DelegatedStatefulService(
            StatefulServiceContext context,
            Action<IServiceCollection> configureServices) : base(context)
        {
            _configureServices = configureServices;
        }

        protected override Task OnOpenAsync(ReplicaOpenMode openMode, CancellationToken cancellationToken)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ServiceContext>(Context);
            services.AddSingleton(Context);
            _configureServices(services);

            services.AddSingleton(StateManager);

            _container = services.BuildServiceProvider();
            
            // This requires the ServiceContext up a few lines, so we can't inject it in the constructor
            _container.GetService<TemporaryFiles>()?.Initialize();

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
            if (_container == null)
            {
                throw new InvalidOperationException("CreateServiceReplicaListeners called before OnOpenAsync");
            }

            Type[] interfaces = typeof(TServiceImplementation).GetAllInterfaces()
                .Where(iface => typeof(IService).IsAssignableFrom(iface))
                .ToArray();
            if (interfaces.Length == 0)
            {
                return Enumerable.Empty<ServiceReplicaListener>();
            }

            return new[]
            {
                new ServiceReplicaListener(
                    context => ServiceHostRemoting.CreateServiceRemotingListener<TServiceImplementation>(
                        context,
                        interfaces,
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
            if (_container == null)
            {
                throw new InvalidOperationException("RunAsync called before OnOpenAsync");
            }
            while (!cancellationToken.IsCancellationRequested)
            {
                using IServiceScope scope = _container.CreateScope();

                TServiceImplementation impl = scope.ServiceProvider.GetRequiredService<TServiceImplementation>();

                TimeSpan shouldWaitFor = await impl.RunAsync(cancellationToken);

                if (shouldWaitFor.Equals(TimeSpan.MaxValue))
                {
                    return;
                }

                await Task.Delay(shouldWaitFor, cancellationToken);
            }
        }

        private async Task RunSchedule(CancellationToken cancellationToken)
        {
            if (_container == null)
            {
                throw new InvalidOperationException("RunAsync called before OnOpenAsync");
            }
            await ScheduledService<TServiceImplementation>.RunScheduleAsync(_container, cancellationToken);
        }
     }
}
