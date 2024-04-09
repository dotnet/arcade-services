// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Tests;

public class JobsProcessorScopeManagerTests
{
    private readonly IServiceProvider _serviceProvider;

    public JobsProcessorScopeManagerTests()
    {
        ServiceCollection services = new();

        services.AddSingleton(new Mock<ITelemetryRecorder>().Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Test, CancelAfter(30000)]
    public async Task JobsProcessorStatusNormalFlow()
    {
        JobScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<JobScopeManager>>());
        // When it starts, the processor is not initializing
        scopeManager.State.Should().Be(JobsProcessorState.Initializing);

        // Initialization is done
        scopeManager.InitializingDone();
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        TaskCompletionSource jobCompletion1 = new();
        TaskCompletionSource jobCompletion2 = new();
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady()) { }
            jobCompletion1.SetResult();
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Start the service again
        scopeManager.Start();

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        // Wait for the worker to finish the job
        await jobCompletion1.Task;

        // The JobProcessor is working now, it shouldn't block on anything
        using (scopeManager.BeginJobScopeWhenReady()) { }

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        // Simulate someone calling stop in the middle of a job
        jobCompletion1 = new();
        jobCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginJobScopeWhenReady())
            {
                jobCompletion1.SetResult();
                await jobCompletion2.Task;
            }
        });
        // Wait for the workerTask to start the job
        await jobCompletion1.Task;

        scopeManager.FinishJobAndStop();

        // Before the job is finished, we should be in the Stopping stage
        scopeManager.State.Should().Be(JobsProcessorState.Stopping);

        // Let the job finish
        jobCompletion2.SetResult();

        await workerTask;

        // Now we should be in the stopped state
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);
    }

    [Test, CancelAfter(30000)]
    public async Task JobsProcessorMultipleStopFlow()
    {
        JobScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<JobScopeManager>>());

        scopeManager.InitializingDone();
        // The jobs processor should start in a stopped state
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        scopeManager.FinishJobAndStop();

        // We were already stopped, so we should continue to be so
        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        TaskCompletionSource jobCompletion = new();

        // Start a new job that should get blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady())
            {
                jobCompletion.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        scopeManager.Start();
        // Wait for the worker to unblock and start the job
        await jobCompletion.Task;

        scopeManager.State.Should().Be(JobsProcessorState.Working);
    }

    [Test, CancelAfter(30000)]
    public async Task JobsProcessorMultipleStartStop()
    {
        JobScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<JobScopeManager>>());

        scopeManager.InitializingDone();

        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        // Start the JobsProcessor multiple times in a row
        scopeManager.Start();
        scopeManager.Start();
        scopeManager.Start();

        scopeManager.State.Should().Be(JobsProcessorState.Working);

        TaskCompletionSource jobCompletion1 = new();
        TaskCompletionSource jobCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginJobScopeWhenReady())
            {
                jobCompletion1.SetResult();
                await jobCompletion2.Task;
            }
        });

        scopeManager.Start();

        await jobCompletion1.Task;

        // Now stop in the middle of a job
        scopeManager.FinishJobAndStop();

        scopeManager.State.Should().Be(JobsProcessorState.Stopping);

        jobCompletion2.SetResult();

        // Wait for the job to finish
        await workerTask;

        scopeManager.State.Should().Be(JobsProcessorState.Stopped);

        // Verify that the new job will actually be blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginJobScopeWhenReady()) { }
        });
        t.Start();

        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Unblock the worker thread
        scopeManager.Start();
    }
}
