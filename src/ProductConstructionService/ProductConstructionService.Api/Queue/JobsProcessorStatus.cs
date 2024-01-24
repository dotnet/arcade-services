// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public enum JobsProcessorState
{
    Working,
    StoppedWorking,
    FinishingJobAndStopping
}

public class JobsProcessorStatus
{
    public JobsProcessorStatus()
    {
        State = JobsProcessorState.Working;
        Semaphore = new(1);
    }

    public void Reset()
    {
        State = JobsProcessorState.Working;
        if (Semaphore.CurrentCount == 0)
        {
            Semaphore.Release();
        }
    }

    public JobsProcessorState State { get; set; }
    public SemaphoreSlim Semaphore { get; }
}
