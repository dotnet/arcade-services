// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public abstract class Lifecycle
    {
        public virtual void OnStarting()
        {
        }

        public virtual Task OnStartingAsync() => Task.CompletedTask;

        public virtual void OnStopping()
        {
        }

        public virtual Task OnStoppingAsync() => Task.CompletedTask;

        public static async Task OnStartingAsync(IServiceProvider services)
        {
            var lifecycles = services.GetServices<Lifecycle>();
            if (lifecycles != null)
            {
                IEnumerable<Lifecycle> lifecyleArray = lifecycles.ToList();
                await Task.WhenAll(lifecyleArray.Select(l => l.OnStartingAsync()));
                foreach (var lifecycle in lifecyleArray)
                {
                    lifecycle.OnStarting();
                }
            }
        }

        public static async Task OnStoppingAsync(IServiceProvider services)
        {
            var lifecycles = services.GetServices<Lifecycle>();
            if (lifecycles != null)
            {
                IEnumerable<Lifecycle> lifecycleList = lifecycles.ToList();
                foreach (var lifecycle in lifecycleList)
                {
                    try
                    {
                        lifecycle.OnStopping();
                    }
                    catch (Exception e)
                    {
                        // It's important that if we are stopping that we not crash other cleanup operations
                        // However, it's a bit too late to log, so Console and hope
                        Console.WriteLine($"EXCEPTION IN LIFECYCLE.ONSTOPPING: {e}");
                    }
                }
                try
                {
                    await Task.WhenAll(lifecycleList.Select(l => l.OnStoppingAsync()));
                }
                catch (Exception e)
                {
                    // It's important that if we are stopping that we not crash other cleanup operations
                    // However, it's a bit too late to log, so Console and hope
                    Console.WriteLine($"EXCEPTION IN LIFECYCLE.ONSTOPPINGASYNC: {e}");
                }
            }
        }
    }
}
