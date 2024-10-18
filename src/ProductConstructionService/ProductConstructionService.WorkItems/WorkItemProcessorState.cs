// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemProcessorState
{
    private readonly WorkItemProcessorStateWriter _stateWriter;
    private readonly AutoResetEvent _autoResetEvent;

    public WorkItemProcessorState(
        AutoResetEvent autoResetEvent,
        WorkItemProcessorStateWriter stateWriter)
    {
        _autoResetEvent = autoResetEvent;
        _stateWriter = stateWriter;
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
        await _stateWriter.SetStateAsync(Working);
    }

    public async Task SetInitializingAsync()
    {
        await _stateWriter.SetStateAsync(Initializing);
    }

    public async Task ReturnWhenWorkingAsync(int pollingRateSeconds)
    {
        string? status;
        do
        {
            status = await _stateWriter.GetStateAsync();
        } while (_autoResetEvent.WaitIfTrue(() => status == Stopped, pollingRateSeconds));
    }

    public async Task SetStoppedIfStoppingAsync()
    {
        var status = await _stateWriter.GetStateAsync();
        if (!string.IsNullOrEmpty(status))
        {
            if (status == Stopping)
            {
                await _stateWriter.SetStateAsync(Stopped);
            }
        }
    }

    public async Task InitializationFinished()
    {
        var status = await _stateWriter.GetStateAsync();
        if (!string.IsNullOrEmpty(status) && status == Initializing)
        {
            await _stateWriter.SetStateAsync(Stopped);
        }
    }

    public async Task FinishWorkItemAndStopAsync()
    {
        var status = await _stateWriter.GetStateAsync();
        if (string.IsNullOrEmpty(status) || status == Working || status == Initializing)
        {
            await _stateWriter.SetStateAsync(Stopping);
        }
    }

    public async Task<string> GetStateAsync()
    {
        return await _stateWriter.GetStateAsync() ?? Stopped;
    }
}
