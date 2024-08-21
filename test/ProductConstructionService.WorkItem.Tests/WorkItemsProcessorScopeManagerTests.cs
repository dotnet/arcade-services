// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.WorkItem.Tests;

public class WorkItemsProcessorScopeManagerTests
{
    private readonly IServiceProvider _serviceProvider;

    public WorkItemsProcessorScopeManagerTests()
    {
        ServiceCollection services = new();

        services.AddSingleton(new Mock<ITelemetryRecorder>().Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorStatusNormalFlow()
    {
        WorkItemScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());
        // When it starts, the processor is not initializing
        scopeManager.State.Should().Be(WorkItemProcessorState.Initializing);

        // Initialization is done
        scopeManager.InitializingDone();
        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();
        Thread t = new(() =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReady()) { }
            workItemCompletion1.SetResult();
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Start the service again
        scopeManager.Start();

        scopeManager.State.Should().Be(WorkItemProcessorState.Working);

        // Wait for the worker to finish the workItem
        await workItemCompletion1.Task;

        // The WorkItemProcessor is working now, it shouldn't block on anything
        using (scopeManager.BeginWorkItemScopeWhenReady()) { }

        scopeManager.State.Should().Be(WorkItemProcessorState.Working);

        // Simulate someone calling stop in the middle of a workItem
        workItemCompletion1 = new();
        workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReady())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });
        // Wait for the workerTask to start the workItem
        await workItemCompletion1.Task;

        scopeManager.FinishWorkItemAndStop();

        // Before the workItem is finished, we should be in the Stopping stage
        scopeManager.State.Should().Be(WorkItemProcessorState.Stopping);

        // Let the workItem finish
        workItemCompletion2.SetResult();

        await workerTask;

        // Now we should be in the stopped state
        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStopFlow()
    {
        WorkItemScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        scopeManager.InitializingDone();
        // The workItems processor should start in a stopped state
        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);

        scopeManager.FinishWorkItemAndStop();

        // We were already stopped, so we should continue to be so
        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);

        TaskCompletionSource workItemCompletion = new();

        // Start a new workItem that should get blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReady())
            {
                workItemCompletion.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        scopeManager.Start();
        // Wait for the worker to unblock and start the workItem
        await workItemCompletion.Task;

        scopeManager.State.Should().Be(WorkItemProcessorState.Working);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStartStop()
    {
        WorkItemScopeManager scopeManager = new(true, _serviceProvider, Mock.Of<ILogger<WorkItemScopeManager>>());

        scopeManager.InitializingDone();

        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);

        // Start the WorkItemsProcessor multiple times in a row
        scopeManager.Start();
        scopeManager.Start();
        scopeManager.Start();

        scopeManager.State.Should().Be(WorkItemProcessorState.Working);

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReady())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });

        scopeManager.Start();

        await workItemCompletion1.Task;

        // Now stop in the middle of a workItem
        scopeManager.FinishWorkItemAndStop();

        scopeManager.State.Should().Be(WorkItemProcessorState.Stopping);

        workItemCompletion2.SetResult();

        // Wait for the workItem to finish
        await workerTask;

        scopeManager.State.Should().Be(WorkItemProcessorState.Stopped);

        // Verify that the new workItem will actually be blocked
        Thread t = new(() =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReady()) { }
        });
        t.Start();

        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Unblock the worker thread
        scopeManager.Start();
    }
}
