// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Maestro.Web
{
    public interface IBackgroundWorkItem
    {
        Task ProcessAsync(JToken argumentToken);
    }

    public class BackgroundQueue : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BlockingCollection<(Type type, JToken args)> _workItems = new BlockingCollection<(Type type, JToken args)>();

        public BackgroundQueue(IServiceScopeFactory scopeFactory,
            ILogger<BackgroundQueue> logger)
        {
            _scopeFactory = scopeFactory;
            Logger = logger;
        }

        public ILogger<BackgroundQueue> Logger { get; }

        public void Post<T>() where T : IBackgroundWorkItem
        {
            Post<T>("");
        }

        public void Post<T>(JToken args) where T : IBackgroundWorkItem
        {
            Logger.LogInformation(
                $"Posted work to BackgroundQueue: {typeof(T).Name}.ProcessAsync({args.ToString(Formatting.None)})");
            _workItems.Add((typeof(T), args));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Get off the synchronous chain from WebHost.Start
            await Task.Yield();
            using (Logger.BeginScope("Processing Background Queue"))
            {
                while (true)
                {
                    try
                    {
                        while (!_workItems.IsCompleted)
                        {
                            if (stoppingToken.IsCancellationRequested)
                            {
                                _workItems.CompleteAdding();
                            }

                            if (_workItems.TryTake(out (Type type, JToken args) item, 1000))
                            {
                                using (Logger.BeginScope("Executing background work: {item} ({args})", item.type.Name, item.args.ToString(Formatting.None)))
                                using (IServiceScope scope = _scopeFactory.CreateScope())
                                {
                                    try
                                    {
                                        var instance = (IBackgroundWorkItem) ActivatorUtilities.CreateInstance(scope.ServiceProvider, item.type);
                                        await instance.ProcessAsync(item.args);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.LogError(
                                            ex,
                                            "Background work {item} threw an unhandled exception.",
                                            item.ToString());
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Background queue got unhandled exception.");
                        continue;
                    }

                    return;
                }
            }
        }
    }
}
