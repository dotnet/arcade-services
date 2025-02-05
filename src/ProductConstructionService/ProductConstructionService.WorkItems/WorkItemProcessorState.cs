// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemProcessorState
{
    private readonly WorkItemProcessorStateCache _stateCache;
    private readonly AutoResetEvent _autoResetEvent;

    public WorkItemProcessorState(
        AutoResetEvent autoResetEvent,
        WorkItemProcessorStateCache stateCache)
    {
        _autoResetEvent = autoResetEvent;
        _stateCache = stateCache;
    }

    /// <summary>
    /// The processor is waiting for service to fully initialize
    /// </summary>
    public const string Initializing = "Initializing";

    /// <summary>
    /// The processor will keep taking and processing new work items
    /// </summary>
    public const string Working = "Working";

    /// <summary>
    /// The processor isn't doing anything
    /// </summary>
    public const string Stopped = "Stopped";

    /// <summary>
    /// The processor will finish its current work item and stop
    /// </summary>
    public const string Stopping = "Stopping";

    public async Task SetStartAsync()
    {
        await _stateCache.SetStateAsync(Working);
    }

    public async Task SetInitializingAsync()
    {
        await _stateCache.SetStateAsync(Initializing);
    }

    public async Task ReturnWhenWorkingAsync(int pollingRateSeconds)
    {
        string? status;
        do
        {
            status = await _stateCache.GetStateAsync();
        } while (_autoResetEvent.WaitIfTrue(() => status != Working, pollingRateSeconds));
    }

    public async Task SetStoppedIfStoppingAsync()
    {
        var status = await _stateCache.GetStateAsync();
        if (!string.IsNullOrEmpty(status))
        {
            if (status == Stopping)
            {
                await _stateCache.SetStateAsync(Stopped);
            }
        }
    }

    public async Task InitializationFinished() => await SetStartAsync();

    public async Task FinishWorkItemAndStopAsync()
    {
        var status = await _stateCache.GetStateAsync();
        if (string.IsNullOrEmpty(status) || status == Working || status == Initializing)
        {
            await _stateCache.SetStateAsync(Stopping);
        }
    }

    public async Task<string> GetStateAsync()
    {
        return await _stateCache.GetStateAsync() ?? Working;
    }
}
