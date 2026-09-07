// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;

namespace Maestro.WorkItems;

public interface IWorkItemProcessorReplicaProvider
{
    /// <summary>
    /// Returns the names of the replicas currently reported for the given revision.
    /// The list is refreshed on every call, replicas can come and go during a deployment.
    /// </summary>
    Task<IReadOnlyList<string>> GetReplicaNamesAsync(string? revisionName = null);
}

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
}

public class LocalWorkItemProcessorReplicaProvider : IWorkItemProcessorReplicaProvider
{
    public Task<IReadOnlyList<string>> GetReplicaNamesAsync(string? revisionName = null)
        => Task.FromResult<IReadOnlyList<string>>([WorkItemConfiguration.LocalReplicaName]);
}
