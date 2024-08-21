// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.WorkItems;

public enum WorkItemProcessorState
{
    /// <summary>
    /// The processor is waiting for service to fully initialize
    /// </summary>
    Initializing,

    /// <summary>
    /// The processor will keep taking and processing new work items
    /// </summary>
    Working,

    /// <summary>
    /// The processor isn't doing anything
    /// </summary>
    Stopped,

    /// <summary>
    /// The processor will finish its current work item and stop
    /// </summary>
    Stopping,
}
