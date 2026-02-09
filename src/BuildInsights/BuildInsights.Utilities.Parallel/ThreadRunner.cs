// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.Utilities.Parallel;

public class ThreadRunner
{
    private readonly ILogger<ThreadRunner> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ParallelismSettings> _settings;

    public ThreadRunner(ILogger<ThreadRunner> logger, IServiceScopeFactory scopeFactory, IOptions<ParallelismSettings> settings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _settings = settings;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        int count = _settings.Value.WorkerCount;
        List<Task> workers = Enumerable
            .Range(0, count)
            .Select(i => Task.Run(() => RunThread(i, cancellationToken), cancellationToken))
            .ToList();

        var stopwatch = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested)
        {
            Task returnedWorker = await Task.WhenAny(workers);
            if (!cancellationToken.IsCancellationRequested)
            {
                if (returnedWorker.IsFaulted)
                {
                    _logger.LogError(returnedWorker.Exception, "Worker faulted with");
                }
                else
                {
                    _logger.LogError("Worker exited unexpected without error");
                }
                var crashLoopDelay = TimeSpan.FromSeconds(_settings.Value.CrashLoopDelaySeconds);
                if (stopwatch.Elapsed < crashLoopDelay)
                {
                    await Task.Delay(crashLoopDelay, cancellationToken);
                }
                stopwatch.Restart();
            }

            workers.Remove(returnedWorker);
            workers.Add(RunThread(count++, cancellationToken));
        }

        await Task.WhenAll(workers);
    }

    private async Task RunThread(int index, CancellationToken cancellationToken)
    {
        using (var childScope = _scopeFactory.CreateScope())
        {
            childScope.SetProcessingThreadIdentity(index.ToString());
            var thread = childScope.ServiceProvider.GetRequiredService<IProcessingThread>();
            await thread.RunAsync(cancellationToken);
        }
    }
}
