// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.WorkItems;

namespace ProductConstructionService.Api.Configuration;

public static class WorkItemProcessorStateInitialization
{
    /// <summary>
    /// Local bootstrap of the control plane side of the protocol. Deployed environments get their desired
    /// state from the deployment, locally there is nothing else that would write it.
    /// </summary>
    public static async Task SetWorkItemProcessorInitialState(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var stateStore = app.Services.GetRequiredService<IWorkItemProcessorStateStore>();
        var replicaName = app.Services.GetRequiredService<WorkItemProcessorReplicaName>();

        await stateStore.SetDesiredStateAsync(replicaName.Value, WorkItemProcessorState.Working, CancellationToken.None);
    }
}
