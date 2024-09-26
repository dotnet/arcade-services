// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ProductConstructionService.Common;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.WorkItem.Tests;

public class WorkItemsProcessorScopeManagerTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();

        services.AddSingleton(new Mock<ITelemetryRecorder>().Object);
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton<WorkItemProcessorRegistrations>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorStatusNormalFlow()
    {
        Mock<IRedisCacheFactory> cacheFactory = new();
        cacheFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(new FakeRedisCache());

        WorkItemProcessorState state = new(cacheFactory.Object, string.Empty);
        WorkItemScopeManager scopeManager = new(_serviceProvider, state, true, -1);
        // When it starts, the processor is not initializing
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Initializing);

        // Initialization is done
        await state.InitializingDoneAsync();
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();
        Thread t = new(() =>
        {
            using (scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }
            workItemCompletion1.SetResult();
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Start the service again
        await state.StartAsync();

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        // Unblock the worker thread
        state.Signal();

        // Wait for the worker to finish the workItem
        await workItemCompletion1.Task;

        // The WorkItemProcessor is working now, it shouldn't block on anything
        await using (await scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        // Simulate someone calling stop in the middle of a workItem
        workItemCompletion1 = new();
        workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            await using (await scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });
        // Wait for the workerTask to start the workItem
        await workItemCompletion1.Task;

        await state.FinishWorkItemAndStopAsync();

        // Before the workItem is finished, we should be in the Stopping stage
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopping);

        // Let the workItem finish
        workItemCompletion2.SetResult();

        await workerTask;

        // Now we should be in the stopped state
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStopFlow()
    {
        Mock<IRedisCacheFactory> cacheFactory = new();
        cacheFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(new FakeRedisCache());

        WorkItemProcessorState state = new(cacheFactory.Object, string.Empty);
        WorkItemScopeManager scopeManager = new(_serviceProvider, state, true, -1);

        await state.InitializingDoneAsync();
        // The workItems processor should start in a stopped state
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        await state.FinishWorkItemAndStopAsync();

        // We were already stopped, so we should continue to be so
        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        TaskCompletionSource workItemCompletion = new();

        // Start a new workItem that should get blocked
        Thread t = new(async () =>
        {
            await using (await scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        await state.StartAsync();
        state.Signal();
        // Wait for the worker to unblock and start the workItem
        await workItemCompletion.Task;

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStartStop()
    {
        Mock<IRedisCacheFactory> cacheFactory = new();
        cacheFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(new FakeRedisCache());

        WorkItemProcessorState state = new(cacheFactory.Object, string.Empty);
        WorkItemScopeManager scopeManager = new(_serviceProvider, state, true, -1);

        await state.InitializingDoneAsync();

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        // Start the WorkItemsProcessor multiple times in a row
        await state.StartAsync();
        await state.StartAsync();
        await state.StartAsync();

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            await using (await scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });

        await state.StartAsync();

        await workItemCompletion1.Task;

        // Now stop in the middle of a workItem
        await state.FinishWorkItemAndStopAsync();

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopping);

        workItemCompletion2.SetResult();

        // Wait for the workItem to finish
        await workerTask;

        (await state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        // Verify that the new workItem will actually be blocked
        Thread t = new(async () =>
        {
            await using (await scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }
        });
        t.Start();

        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Unblock the worker thread
        state.Signal();
        await state.StartAsync();
    }
}
