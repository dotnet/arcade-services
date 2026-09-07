// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Services.Common.Cache;
using Microsoft.Extensions.Logging;

namespace Maestro.WorkItems;

/// <summary>
/// Control plane view of the queue processing state of a replica.
/// Only deployment or an explicit operator action writes the desired state,
/// only the replica itself writes the observed state.
/// </summary>
public interface IWorkItemProcessorStateStore
{
    Task<WorkItemProcessorState?> GetDesiredStateAsync(
        string replicaName,
        CancellationToken cancellationToken);

    Task SetDesiredStateAsync(
        string replicaName,
        WorkItemProcessorState state,
        CancellationToken cancellationToken);

    Task<WorkItemProcessorState?> GetObservedStateAsync(
        string replicaName,
        CancellationToken cancellationToken);

    Task SetObservedStateAsync(
        string replicaName,
        WorkItemProcessorState state,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string replicaName,
        CancellationToken cancellationToken);
}

public class WorkItemProcessorStateStore : IWorkItemProcessorStateStore
{
    private const string QueueProcessingStateKeyPrefix = "queue-processing-state";

    private readonly IRedisCacheFactory _redisCacheFactory;
    private readonly ILogger<WorkItemProcessorStateStore> _logger;

    public WorkItemProcessorStateStore(
        IRedisCacheFactory redisCacheFactory,
        ILogger<WorkItemProcessorStateStore> logger)
    {
        _redisCacheFactory = redisCacheFactory;
        _logger = logger;
    }

    public static string GetDesiredKey(string replicaName) =>
        $"{QueueProcessingStateKeyPrefix}:{replicaName}:desired";

    public static string GetObservedKey(string replicaName) =>
        $"{QueueProcessingStateKeyPrefix}:{replicaName}:observed";

    public Task<WorkItemProcessorState?> GetDesiredStateAsync(string replicaName, CancellationToken cancellationToken)
        => ReadStateAsync(GetDesiredKey(replicaName), cancellationToken);

    public Task SetDesiredStateAsync(string replicaName, WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting desired state of replica {replicaName} to {state}", replicaName, state);
        return WriteStateAsync(GetDesiredKey(replicaName), state, cancellationToken);
    }

    public Task<WorkItemProcessorState?> GetObservedStateAsync(string replicaName, CancellationToken cancellationToken)
        => ReadStateAsync(GetObservedKey(replicaName), cancellationToken);

    public Task SetObservedStateAsync(string replicaName, WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Replica {replicaName} reporting observed state {state}", replicaName, state);
        return WriteStateAsync(GetObservedKey(replicaName), state, cancellationToken);
    }

    public async Task DeleteAsync(string replicaName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Deleting queue processing state keys of replica {replicaName}", replicaName);
        await _redisCacheFactory.Create(GetDesiredKey(replicaName)).TryDeleteAsync();
        await _redisCacheFactory.Create(GetObservedKey(replicaName)).TryDeleteAsync();
    }

    private async Task<WorkItemProcessorState?> ReadStateAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await _redisCacheFactory.Create(key).TryGetAsync();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!Enum.TryParse(value, out WorkItemProcessorState state))
        {
            _logger.LogWarning("Key {key} holds unrecognized queue processing state {value}", key, value);
            return null;
        }

        return state;
    }

    private async Task WriteStateAsync(string key, WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _redisCacheFactory.Create(key).SetAsync(state.ToString());
    }
}
