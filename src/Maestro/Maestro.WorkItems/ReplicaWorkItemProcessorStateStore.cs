// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.WorkItems;

/// <summary>
/// Name of the replica this process runs as.
/// </summary>
public sealed record WorkItemProcessorReplicaName(string Value);

/// <summary>
/// Replica side of the two-key protocol. A running replica may only read its desired
/// state and report its own observed state, it can never issue a desired state command.
/// </summary>
public interface IReplicaWorkItemProcessorStateStore
{
    Task<WorkItemProcessorState?> GetDesiredStateAsync(CancellationToken cancellationToken);

    Task SetObservedStateAsync(WorkItemProcessorState state, CancellationToken cancellationToken);
}

public class ReplicaWorkItemProcessorStateStore : IReplicaWorkItemProcessorStateStore
{
    private readonly IWorkItemProcessorStateStore _stateStore;
    private readonly WorkItemProcessorReplicaName _replicaName;

    public ReplicaWorkItemProcessorStateStore(IWorkItemProcessorStateStore stateStore, WorkItemProcessorReplicaName replicaName)
    {
        _stateStore = stateStore;
        _replicaName = replicaName;
    }

    public Task<WorkItemProcessorState?> GetDesiredStateAsync(CancellationToken cancellationToken)
        => _stateStore.GetDesiredStateAsync(_replicaName.Value, cancellationToken);

    public Task SetObservedStateAsync(WorkItemProcessorState state, CancellationToken cancellationToken)
        => _stateStore.SetObservedStateAsync(_replicaName.Value, state, cancellationToken);
}
