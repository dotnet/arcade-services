// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemScopeManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkItemProcessorState _state;
    private readonly int _pollingRateSeconds;

    private int _activeWorkItems = 0;

    public WorkItemScopeManager(
        IServiceProvider serviceProvider,
        WorkItemProcessorState state,
        int pollingRateSeconds)
    {
        _serviceProvider = serviceProvider;
        _state = state;
        _pollingRateSeconds = pollingRateSeconds;
    }

    /// <summary>
    /// Creates a new scope for the currently executing WorkItem, when the the WorkItemsProcessor is in the `Working` state.
    /// </summary>
    public async Task<WorkItemScope> BeginWorkItemScopeWhenReadyAsync()
    {
        await _state.ReturnWhenWorkingAsync(_pollingRateSeconds);

        var scope = _serviceProvider.CreateScope();
        Interlocked.Increment(ref _activeWorkItems);

        return new WorkItemScope(
            scope.ServiceProvider.GetRequiredService<IOptions<WorkItemProcessorRegistrations>>(),
            WorkItemFinishedAsync,
            scope);
    }

    private async Task WorkItemFinishedAsync()
    {
        if (Interlocked.Decrement(ref _activeWorkItems) == 0)
        {
            await _state.SetStoppedIfStoppingAsync();
        }
    }

    public async Task InitializationFinished()
    {
        await _state.InitializationFinished();
    }

    public async Task<string> GetStateAsync() => await _state.GetStateAsync();
}
