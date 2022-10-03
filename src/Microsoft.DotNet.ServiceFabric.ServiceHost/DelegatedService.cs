// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost;

public static class DelegatedService
{
    public static async Task RunServiceLoops<T>(IServiceProvider services, CancellationToken serviceFabricShutdownToken, params Func<CancellationToken, Task>[] loops)
    {
        // Services run in a set of "loops". Normally this is the "RunAsync" loop, and the "Scheulded" loop for Cron jobs
        // Normal expectation is that these loops should never exit before being told to,
        // since that means the service is no longer servicing
        // The way we've piped that "being told to" through in our framework
        // is via the cancellation token (serviceFabricShutdownToken).
        // So a "good" exit is when every task responds to that token and throws an OperationCancellationException
        // relaying that information (via token.ThrowIfCancellationRequested)
        // Any process exiting without that, or with some OTHER exception means something abnormal has happened
        // and we need to shutdown all the other loops so that we can restart the process to hopefully fix the problem
        using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(serviceFabricShutdownToken);
        await Lifecycle.OnStartingAsync(services);
        var logger = services.GetRequiredService<ILogger<T>>();
        List<Task> pendingTasks = new List<Task>();
        try
        {
            await using var _ =
                serviceFabricShutdownToken.Register(() => logger.LogInformation("Service abort cancellation requested"));
            logger.LogInformation("Entering service 'RunAsync'");
            pendingTasks = loops.Select(l => l(linkedToken.Token)).ToList();
            var finished = await Task.WhenAny(pendingTasks);
            pendingTasks.Remove(finished);
            await finished;
            logger.LogWarning("Abnormal service exit without cancellation");
        }
        catch (OperationCanceledException) when (serviceFabricShutdownToken.IsCancellationRequested)
        {
            // ONE of the tasks cancelled, but we need to wait for the others
            var killTimer = Task.Delay(TimeSpan.FromSeconds(15));
            pendingTasks.Add(killTimer);
            // While there is more than the kill timer waiting to be completed
            while (pendingTasks.Count > 1)
            {
                try
                {
                    var completed = await Task.WhenAny(pendingTasks);
                    pendingTasks.Remove(completed);
                    if (completed == killTimer)
                    {
                        // Times up...
                        logger.LogCritical(
                            "When cancelling, other loops did not exit in 15 seconds, abandoning"
                        );
                        break;
                    }
                    // The first await was just to get the task that was no longer running
                    // now we need to await it to get it's exception/result
                    await completed;
                    logger.LogWarning("During cancellation, abnormal service exit without cancellation");
                }
                catch (OperationCanceledException e2) when (e2.CancellationToken == linkedToken.Token)
                {
                    // Other guy cancelled normally, goodness, move on
                }
                catch (Exception e2)
                {
                    logger.LogCritical(e2, "During cancellation exit, a loop threw an unhandled exception");
                }
            }

            logger.LogInformation("Normal service shutdown complete");
            // This should rethrow and exit back to SF
            serviceFabricShutdownToken.ThrowIfCancellationRequested();
        }
        catch (Exception e)
        {
            // ONE of the tasks crashed, we need to stop the other, if we can
            linkedToken.Cancel();
            var killTimer = Task.Delay(TimeSpan.FromSeconds(15));
            pendingTasks.Add(killTimer);
            // While there is more than the kill timer waiting to be completed
            while (pendingTasks.Count > 1)
            {
                try
                {
                    var completed = await Task.WhenAny(pendingTasks);
                    pendingTasks.Remove(completed);
                    if (completed == killTimer)
                    {
                        // Times up...
                        logger.LogCritical(
                            "When exiting because of unhandled exception, other loops did not exit in 15 seconds, abandoning"
                        );
                        break;
                    }
                    // The first await was just to get the task that was no longer running
                    // now we need to await it to get it's exception/result
                    await completed;
                    logger.LogWarning("During unhandled exception, abnormal service exit without cancellation");
                }
                catch (OperationCanceledException e2) when (e2.CancellationToken == linkedToken.Token)
                {
                    // Other guy cancelled normally, goodness, move on
                }
                catch (Exception e2)
                {
                    logger.LogCritical(e2, "During crash exit, another loop threw an unhandled exception");
                }
            }

            logger.LogCritical(e, "Unhandled exception crashing service execution");
            throw;
        }
        finally
        {
            await Lifecycle.OnStoppingAsync(services);
        }
    }
}
