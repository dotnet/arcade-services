// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Tests;

public class JobsProcessorStatusTests
{
    [Test, CancelAfter(1000)]
    public async Task JobsProcessorStatusNormalFlow()
    {
        AutoResetEvent autoResetEvent = new(true);
        JobsProcessorScopeManager scopeManager = new();

        // When it starts, the processor is not working
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        TaskCompletionSource tcs1 = new();
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None))
            {
                tcs1.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Start the service again
        scopeManager.Start();

        // Wait for the worker to enter the job
        await tcs1.Task;

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        // The JobProcessor is working now, it shouldn't block on anything
        await Task.Factory.StartNew(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None)) { }
        });

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        // Simulate someone calling stop in the middle of a job
        tcs1 = new();
        TaskCompletionSource tsc2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None))
            {
                tcs1.SetResult();
                await tsc2.Task;
            }
        });
        // Wait for the workerTask to start the job
        await tcs1.Task;

        scopeManager.FinishJobAndStop();

        // Before the job is finished, we should be in the Stopping stage
        scopeManager.State.Should().Be(JobsProcessorState.Stopping);

        // Let the job finish
        tsc2.SetResult();

        await workerTask;

        // Now we should be in the stopped state
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);
    }

    [Test, CancelAfter(1000)]
    public async Task JobsProcessorMultipleStopFlow()
    {
        JobsProcessorScopeManager scopeManager = new();

        // The jobs processor should start in a stopped state
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        scopeManager.FinishJobAndStop();

        // We were already stopped, so we should continue to be so
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        TaskCompletionSource tcs = new();

        // Start a new job that should get blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None))
            {
                tcs.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        scopeManager.Start();
        // Wait for the worker to unblock and start the job
        await tcs.Task;

        scopeManager.State.Should().Be(JobsProcessorState.Working);
    }

    [Test, CancelAfter(1000)]
    public async Task JobsProcessorMultipleStartStop()
    {
        JobsProcessorScopeManager scopeManager = new();

        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        // Start the JobsProcessor multiple times in a row
        scopeManager.Start();
        scopeManager.Start();
        scopeManager.Start();

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        TaskCompletionSource tcs1 = new();
        TaskCompletionSource tcs2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None))
            {
                tcs1.SetResult();
                await tcs2.Task;
            }
        });

        await tcs1.Task;

        // Now stop in the middle of a job
        scopeManager.FinishJobAndStop();

        scopeManager.State.Should().Be(JobsProcessorState.Stopping);

        tcs2.SetResult();

        // Wait for the job to finish
        await workerTask;

        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        // Verify that the new job will actually be blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady(CancellationToken.None)) { }
        });
        t.Start();

        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Unblock the worker thread
        scopeManager.Start();
    }
}
