// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ProductConstructionService.Common.Cache;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.WorkItem.Tests;

public class WorkItemsProcessorScopeManagerTests
{
    private IServiceProvider _serviceProvider = null!;
    WorkItemProcessorState _state = null!;
    WorkItemProcessorStateCache _stateCache = null!;
    WorkItemScopeManager _scopeManager = null!;
    AutoResetEvent _autoResetEvent = null!;

    [SetUp]
    public void SetUp()
    {
        ServiceCollection services = new();

        services.AddSingleton(new Mock<ITelemetryRecorder>().Object);
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton<WorkItemProcessorRegistrations>();

        _serviceProvider = services.BuildServiceProvider();

        Mock<IRedisCacheFactory> cacheFactory = new();
        cacheFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(new FakeRedisCache());
        _autoResetEvent = new(false);

        _stateCache = new(
            cacheFactory.Object,
            "testReplica",
            new Mock<ILogger<WorkItemProcessorStateCache>>().Object);

        _state = new(
            _autoResetEvent,
            _stateCache);
        _scopeManager = new(_serviceProvider, _state, -1);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorStatusNormalFlow()
    {
        await _state.SetInitializingAsync();
        // When it starts, the processor is initializing
        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Initializing);

        // Initialization is done
        await _state.InitializationFinished();
        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);
        await _state.FinishWorkItemAndStopAsync();

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();
        Thread t = new(async () =>
        {
            await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }
            workItemCompletion1.SetResult();
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Start the service again
        await _state.SetStartAsync();

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        // Unblock the worker thread
        _autoResetEvent.Set();

        // Wait for the worker to finish the workItem
        await workItemCompletion1.Task;

        // The WorkItemProcessor is working now, it shouldn't block on anything
        await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        // Simulate someone calling stop in the middle of a workItem
        workItemCompletion1 = new();
        workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });
        // Wait for the workerTask to start the workItem
        await workItemCompletion1.Task;

        await _state.FinishWorkItemAndStopAsync();

        // Before the workItem is finished, we should be in the Stopping stage
        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopping);

        // Let the workItem finish
        workItemCompletion2.SetResult();

        await workerTask;

        // Now we should be in the stopped state
        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStopFlow()
    {
        await _state.SetInitializingAsync();

        await _state.InitializationFinished();
        // The workItems processor should start in a stopped state
        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        await _state.FinishWorkItemAndStopAsync();

        TaskCompletionSource workItemCompletion = new();

        // Start a new workItem that should get blocked
        Thread t = new(async () =>
        {
            await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion.SetResult();
            }
        });
        t.Start();

        // Wait for the worker to start and get blocked
        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        await _state.SetStartAsync();
        _autoResetEvent.Set();
        // Wait for the worker to unblock and start the workItem
        await workItemCompletion.Task;

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);
    }

    [Test, CancelAfter(30000)]
    public async Task WorkItemsProcessorMultipleStartStop()
    {
        await _state.SetInitializingAsync();
        await _state.InitializationFinished();

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        // Start the WorkItemsProcessor multiple times in a row
        await _state.SetStartAsync();
        await _state.SetStartAsync();
        await _state.SetStartAsync();

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Working);

        TaskCompletionSource workItemCompletion1 = new();
        TaskCompletionSource workItemCompletion2 = new();

        var workerTask = Task.Run(async () =>
        {
            await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync())
            {
                workItemCompletion1.SetResult();
                await workItemCompletion2.Task;
            }
        });

        await _state.SetStartAsync();

        await workItemCompletion1.Task;

        // Now stop in the middle of a workItem
        await _state.FinishWorkItemAndStopAsync();

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopping);

        workItemCompletion2.SetResult();

        // Wait for the workItem to finish
        await workerTask;

        (await _state.GetStateAsync()).Should().Be(WorkItemProcessorState.Stopped);

        // Verify that the new workItem will actually be blocked
        Thread t = new(async () =>
        {
            await using (await _scopeManager.BeginWorkItemScopeWhenReadyAsync()) { }
        });
        t.Start();

        while (t.ThreadState != ThreadState.WaitSleepJoin) ;

        // Unblock the worker thread
        _autoResetEvent.Set();
        await _state.SetStartAsync();
    }
}
