// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.ResourceManager.AppContainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;
public static class ContainerAppResourceExtension
{
    public static async Task<List<WorkItemProcessorState>> GetRevisionReplicaStatesAsync(
        this ContainerAppResource containerApp,
        string revisionName,
        IRedisCacheFactory redisCacheFactory,
        IServiceProvider serviceProvider)
    {
        var activeRevision = (await containerApp.GetContainerAppRevisionAsync(revisionName)).Value;
        return activeRevision.GetContainerAppReplicas()
            // Without this, VS can't distinguish between Enumerable and AsyncEnumerable in the Select bellow
            .ToEnumerable()
            .Select(replica => new WorkItemProcessorState(
                redisCacheFactory,
                replica.Data.Name,
                new AutoResetEvent(false),
                serviceProvider.GetRequiredService<ILogger<WorkItemProcessorState>>()))
            .ToList();
    }
}
