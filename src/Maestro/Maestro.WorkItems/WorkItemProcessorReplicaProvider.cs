// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;

namespace Maestro.WorkItems;

public interface IWorkItemProcessorReplicaProvider
{
    /// <summary>
    /// Returns the names of the replicas currently reported for the given revision.
    /// The list is refreshed on every call, replicas can come and go during a deployment.
    /// </summary>
    Task<IReadOnlyList<string>> GetReplicaNamesAsync(string? revisionName = null);

    /// <summary>
    /// Refreshes lifecycle evidence for deactivation. Providers without lifecycle information fail closed.
    /// </summary>
    async Task<IReadOnlyList<WorkItemProcessorReplicaStatus>> GetReplicaStatusesAsync(string revisionName)
        => [.. (await GetReplicaNamesAsync(revisionName)).Select(name => new WorkItemProcessorReplicaStatus(name, false))];
}

public sealed record WorkItemProcessorReplicaStatus(string Name, bool HasNeverStarted);

public class ContainerAppWorkItemProcessorReplicaProvider : IWorkItemProcessorReplicaProvider
{
    private ContainerAppResource _containerApp;

    public ContainerAppWorkItemProcessorReplicaProvider(ContainerAppResource containerApp)
    {
        _containerApp = containerApp;
    }

    public async Task<IReadOnlyList<string>> GetReplicaNamesAsync(string? revisionName = null)
    {
        // Always fetch the latest container app information, in case there was a deployment or something like that
        // in between calls
        _containerApp = await _containerApp.GetAsync();

        if (string.IsNullOrEmpty(revisionName))
        {
            revisionName = _containerApp.Data.Configuration.Ingress.Traffic
                .Single(traffic => traffic.Weight == 100)
                .RevisionName;

            if (string.IsNullOrEmpty(revisionName))
            {
                throw new InvalidOperationException("Current active revision has no revision name");
            }
        }

        var revision = await _containerApp.GetContainerAppRevisionAsync(revisionName);

        return [.. revision.Value.GetContainerAppReplicas().AsEnumerable().Select(replica => replica.Data.Name)];
    }

    public async Task<IReadOnlyList<WorkItemProcessorReplicaStatus>> GetReplicaStatusesAsync(string revisionName)
    {
        var revision = (await _containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        var expectedContainers = revision.Data.Template?.Containers.Select(container => container.Name).ToList();

        return [.. revision.GetContainerAppReplicas().AsEnumerable().Select(replica =>
            new WorkItemProcessorReplicaStatus(replica.Data.Name, HasNeverStarted(replica.Data, expectedContainers)))];
    }

    private static bool HasNeverStarted(ContainerAppReplicaData replica, IReadOnlyList<string>? expectedContainers)
    {
        // Readiness/startup-probe flags alone say nothing about whether a process has admitted work.
        // Require complete container coverage and affirmative, consistent lifecycle evidence.
        return expectedContainers is { Count: > 0 }
            && expectedContainers.All(name => !string.IsNullOrEmpty(name) && replica.Containers.Any(container => container.Name == name))
            && replica.Containers.Count > 0
            && replica.Containers.All(container =>
                !string.IsNullOrEmpty(container.Name)
                && string.IsNullOrEmpty(container.ContainerId)
                && container.RestartCount == 0
                && container.RunningState == ContainerAppContainerRunningState.Waiting
                && container.IsStarted == false
                && container.IsReady == false);
    }
}

public class LocalWorkItemProcessorReplicaProvider : IWorkItemProcessorReplicaProvider
{
    public Task<IReadOnlyList<string>> GetReplicaNamesAsync(string? revisionName = null)
        => Task.FromResult<IReadOnlyList<string>>([WorkItemConfiguration.LocalReplicaName]);
}
