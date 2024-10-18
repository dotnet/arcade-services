// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.DependencyFlow.WorkItems;
using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.WorkItemProcessors;

public abstract class DependencyFlowUpdateProcessor<TWorkItem>
    : WorkItemProcessor<TWorkItem> where TWorkItem : DependencyFlowWorkItem
{
    protected override string? GetSynchronizationKey(TWorkItem workItem) => "DependencyUpdate_" + workItem.UpdaterId;

    protected override Dictionary<string, object> GetLoggingContextData(TWorkItem workItem)
    {
        var data = base.GetLoggingContextData(workItem);
        data["UpdaterId"] = workItem.UpdaterId;
        return data;
    }
}
