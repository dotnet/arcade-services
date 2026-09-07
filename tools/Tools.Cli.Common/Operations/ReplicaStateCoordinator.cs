// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.WorkItems;
using Microsoft.Extensions.Logging;

namespace Tools.Cli.Common.Operations;

/// <summary>
/// Control plane side of the desired/observed replica protocol. It only ever writes desired state
/// and only ever reads observed state, and it refreshes the replica list on every poll so that
/// replicas appearing or disappearing during a deployment are handled.
/// </summary>
public class ReplicaStateCoordinator
{
    public const int PollIntervalSeconds = 10;
    public const int MaxPollAttempts = 100;

    private readonly IWorkItemProcessorReplicaProvider _replicaProvider;
    private readonly IWorkItemProcessorStateStore _stateStore;
    private readonly ILogger<ReplicaStateCoordinator> _logger;
    private readonly Dictionary<string, HashSet<string>> _knownReplicasByRevision = new(StringComparer.OrdinalIgnoreCase);

    public ReplicaStateCoordinator(
        IWorkItemProcessorReplicaProvider replicaProvider,
        IWorkItemProcessorStateStore stateStore,
        ILogger<ReplicaStateCoordinator> logger)
    {
        _replicaProvider = replicaProvider;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <summary>
    /// Writes the desired state to every replica of the revision and waits until all of them report it.
    /// Replicas discovered later in the wait receive the same desired state.
    /// </summary>
    public async Task<bool> SetDesiredStateAndWaitAsync(
        string revisionName,
        WorkItemProcessorState desiredState,
        bool requireAtLeastOneReplica,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> knownReplicas = GetKnownReplicas(revisionName);
        HashSet<string> commandedReplicas = new(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "Requesting state {desiredState} for replicas of revision {revisionName}",
            desiredState,
            revisionName);

        for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            IReadOnlyList<string> replicaNames = await _replicaProvider.GetReplicaNamesAsync(revisionName);

            foreach (var replicaName in replicaNames)
            {
                if (commandedReplicas.Add(replicaName))
                {
                    knownReplicas.Add(replicaName);
                    await _stateStore.SetDesiredStateAsync(replicaName, desiredState, cancellationToken);
                }
            }

            if (replicaNames.Count == 0 && !requireAtLeastOneReplica)
            {
                _logger.LogInformation("Revision {revisionName} has no replicas, nothing to wait for", revisionName);
                return true;
            }

            if (replicaNames.Count > 0 && await AllReplicasReportAsync(replicaNames, desiredState, cancellationToken))
            {
                _logger.LogInformation(
                    "All {replicaCount} replicas of revision {revisionName} report state {desiredState}",
                    replicaNames.Count,
                    revisionName,
                    desiredState);
                return true;
            }

            _logger.LogInformation(
                "Waiting for replicas of revision {revisionName} to report state {desiredState}",
                revisionName,
                desiredState);
            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), cancellationToken);
        }

        _logger.LogError(
            "Replicas of revision {revisionName} did not report state {desiredState} within {timeoutSeconds} seconds",
            revisionName,
            desiredState,
            MaxPollAttempts * PollIntervalSeconds);
        return false;
    }

    /// <summary>
    /// Removes the desired and observed keys of every replica the deployment has written to.
    /// Only safe once the revision is deactivated.
    /// </summary>
    public async Task DeleteStateAsync(string revisionName, CancellationToken cancellationToken = default)
    {
        if (!_knownReplicasByRevision.TryGetValue(revisionName, out HashSet<string>? knownReplicas))
        {
            return;
        }

        foreach (var replicaName in knownReplicas)
        {
            await _stateStore.DeleteAsync(replicaName, cancellationToken);
        }

        _knownReplicasByRevision.Remove(revisionName);
    }

    private async Task<bool> AllReplicasReportAsync(
        IReadOnlyList<string> replicaNames,
        WorkItemProcessorState desiredState,
        CancellationToken cancellationToken)
    {
        foreach (var replicaName in replicaNames)
        {
            WorkItemProcessorState? observedState = await _stateStore.GetObservedStateAsync(replicaName, cancellationToken);
            if (observedState != desiredState)
            {
                return false;
            }
        }

        return true;
    }

    private HashSet<string> GetKnownReplicas(string revisionName)
    {
        if (!_knownReplicasByRevision.TryGetValue(revisionName, out HashSet<string>? knownReplicas))
        {
            knownReplicas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _knownReplicasByRevision[revisionName] = knownReplicas;
        }

        return knownReplicas;
    }
}
