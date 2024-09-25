// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public class WorkItemProcessorState
{
    private readonly IRedisCache _cache;
    // After 30 days the replica will be inactive for sure, so we can clean the state
    private static TimeSpan StateExpirationTime = TimeSpan.FromDays(30);

    public WorkItemProcessorState(
        IRedisCacheFactory cacheFactory,
        string replicaName)
    {
        _cache = cacheFactory.Create(replicaName);
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

    private const int WaitTimeSeconds = 10;

    public async Task StartAsync()
    {
        await _cache.SetAsync(Working, StateExpirationTime);
    }

    public async Task StartInitializingAsync()
    {
        await _cache.SetAsync(Initializing, StateExpirationTime);
    }

    public async Task ReturnWhenWorkingAsync()
    {
        string? status;
        do
        {
            status = await _cache.GetAsync();
        } while (await Utility.SleepIfTrue(
            () => string.IsNullOrEmpty(status) || status != Working,
            WaitTimeSeconds));
    }

    public async Task SetStoppedIfStoppingAsync()
    {
        var status = await _cache.GetAsync();
        if (!string.IsNullOrEmpty(status))
        {
            if (status == Stopping)
            {
                await _cache.SetAsync(Stopped, StateExpirationTime);
            }
        }
    }

    public async Task InitializingDoneAsync()
    {
        var status = await _cache.GetAsync();
        if (!string.IsNullOrEmpty(status) && status == Initializing)
        {
            await _cache.SetAsync(Working, StateExpirationTime);
        }
    }

    public async Task FinishWorkItemAndStopAsync()
    {
        var status = await _cache.GetAsync();
        if (string.IsNullOrEmpty(status) || status == Working || status == Initializing)
        {
            await _cache.SetAsync(Stopping, StateExpirationTime);
        }
    }

    public async Task<string> GetStateAsync()
    {
        return await _cache.GetAsync() ?? Stopped;
    }
}
