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
    public class DelegatedStatelessService<TServiceImplementation> : StatelessService
        where TServiceImplementation : IServiceImplementation
    {
        private readonly ServiceProvider _container;

        public DelegatedStatelessService(
            StatelessServiceContext context,
            Action<IServiceCollection> configureServices) : base(context)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ServiceContext>(Context);
            services.AddSingleton(Context);
            configureServices(services);

            _container = services.BuildServiceProvider();

            // This requires the ServiceContext up a few lines, so we can't inject it in the constructor
            _container.GetService<TemporaryFiles>()?.Initialize();
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

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            Type[] ifaces = typeof(TServiceImplementation).GetAllInterfaces()
                .Where(iface => typeof(IService).IsAssignableFrom(iface))
                .ToArray();
            if (ifaces.Length == 0)
            {
                return Enumerable.Empty<ServiceInstanceListener>();
            }

            return new[]
            {
                new ServiceInstanceListener(
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
                using (IServiceScope scope = _container.CreateScope())
                {
                    var impl = scope.ServiceProvider.GetService<TServiceImplementation>();

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