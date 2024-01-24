// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public enum PcsJobsProcessorState
{
    Working,
    StoppedWorking,
    FinishingJobAndStopping
}

public class PcsJobsProcessorStatus
{
    public PcsJobsProcessorStatus()
    {
        State = PcsJobsProcessorState.Working;
        Semaphore = new(1);
    }

    public void Reset()
    {
        State = PcsJobsProcessorState.Working;
        if (Semaphore.CurrentCount == 0)
        {
            Semaphore.Release();
        }
    }

    public PcsJobsProcessorState State { get; set; }
    public SemaphoreSlim Semaphore { get; }
}
