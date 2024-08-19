// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public enum WorkItemProcessorState
{
    /// <summary>
    /// The JobsProcessor is waiting for service to fully initialize
    /// </summary>
    Initializing,

    /// <summary>
    /// The JobsProcessor will keep taking and processing new jobs
    /// </summary>
    Working,

    /// <summary>
    /// The JobsProcessor isn't doing anything
    /// </summary>
    Stopped,

    /// <summary>
    /// The JobsProcessor will finish its current job and stop
    /// </summary>
    Stopping,
}
