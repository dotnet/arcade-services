// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SubscriptionActorService.Tests
{
    public class TestsWithServices : TestsWithMocks
    {
        public TestsWithServices()
        {
            Builder = new ServiceCollection();
        }

        protected ServiceCollection Builder { get; }

        protected virtual Task BeforeExecute(IServiceProvider serviceScope)
        {
            return Task.CompletedTask;
        }

        protected async Task Execute(Func<IServiceProvider, Task> run)
        {
            using (ServiceProvider container = Builder.BuildServiceProvider())
            using (IServiceScope scope = container.GetRequiredService<IServiceScopeFactory>().CreateScope())
            {
                await BeforeExecute(scope.ServiceProvider);
                await run(scope.ServiceProvider);
            }
        }
    }
}
