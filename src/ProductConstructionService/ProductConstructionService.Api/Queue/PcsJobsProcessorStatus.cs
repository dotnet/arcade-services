// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public class PcsJobsProcessorStatus
{
    public PcsJobsProcessorStatus()
    {
        ContinueWorking = true;
        StoppedWorking = false;
        Semaphore = new(1);
    }

    public void Reset()
    {
        ContinueWorking = true;
        StoppedWorking = false;
        if (Semaphore.CurrentCount == 0)
        {
            Semaphore.Release();
        }
    }

    public bool ContinueWorking { get; set; }
    public bool StoppedWorking { get; set; }
    public SemaphoreSlim Semaphore { get; }
}
