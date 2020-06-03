// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SubscriptionActorService.Tests
{
    public class TestsWithServices : TestsWithMocks
    {
        protected virtual void RegisterServices(IServiceCollection services)
        {
        }

        protected virtual Task BeforeExecute(IServiceProvider serviceScope)
        {
            return Task.CompletedTask;
        }

        protected async Task Execute(Func<IServiceProvider, Task> run)
        {
            var services = new ServiceCollection();
            Environment.SetEnvironmentVariable("ENVIRONMENT", "XUNIT");
            services.TryAddSingleton(typeof(IActorProxyFactory<>), typeof(ActorProxyFactory<>));
            RegisterServices(services);
            using (ServiceProvider container = services.BuildServiceProvider())
            using (IServiceScope scope = container.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                await BeforeExecute(scope.ServiceProvider);
                await run(scope.ServiceProvider);
            }
        }
    }
}
