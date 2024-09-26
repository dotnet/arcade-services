// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemProcessorState
{
    private readonly IRedisCache _cache;
    // After 30 days the replica will be inactive for sure, so we can clean the state
    private static TimeSpan StateExpirationTime = TimeSpan.FromDays(30);
    private readonly AutoResetEvent _autoResetEvent;
    private readonly ILogger<WorkItemProcessorState> _logger;

    public string ReplicaName { get; }

    public WorkItemProcessorState(
        IRedisCacheFactory cacheFactory,
        string replicaName,
        AutoResetEvent autoResetEvent,
        ILogger<WorkItemProcessorState> logger)
    {
        _cache = cacheFactory.Create(replicaName);
        ReplicaName = replicaName;
        _autoResetEvent = autoResetEvent;
        _logger = logger;
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

    public async Task StartAsync()
    {
        await ChangeStateAsync(Working);
    }

    public async Task StartInitializingAsync()
    {
        await ChangeStateAsync(Initializing);
    }

    public async Task ReturnWhenWorkingAsync(int pollingRateSeconds)
    {
        string? status;
        do
        {
            status = await _cache.GetAsync();
        } while (_autoResetEvent.WaitIfTrue(() => status == Stopped, pollingRateSeconds));
    }

    public async Task SetStoppedIfStoppingAsync()
    {
        var status = await _cache.GetAsync();
        if (!string.IsNullOrEmpty(status))
        {
            if (status == Stopping)
            {
                await ChangeStateAsync(Stopped);
            }
        }
    }

    public async Task InitializingDoneAsync()
    {
        var status = await _cache.GetAsync();
        if (!string.IsNullOrEmpty(status) && status == Initializing)
        {
            await ChangeStateAsync(Stopped);
        }
    }

    public async Task FinishWorkItemAndStopAsync()
    {
        var status = await _cache.GetAsync();
        if (string.IsNullOrEmpty(status) || status == Working || status == Initializing)
        {
            await ChangeStateAsync(Stopping);
        }
    }

    public async Task<string> GetStateAsync()
    {
        return await _cache.GetAsync() ?? Stopped;
    }

    private async Task ChangeStateAsync(string value)
    {
        _logger.LogInformation("Changing replica {replicaName} state to {state}", ReplicaName, value);
        await _cache.SetAsync(value, StateExpirationTime);
    }
}
