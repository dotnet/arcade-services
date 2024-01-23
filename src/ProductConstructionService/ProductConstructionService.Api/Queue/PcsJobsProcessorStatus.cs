// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public class PcsJobsProcessorStatus
{
    public PcsJobsProcessorStatus()
    {
        ContinueWorking = true;
        Semaphore = new(1);
    }

    public void Reset()
    {
        ContinueWorking = true;
        if (Semaphore.CurrentCount == 0)
        {
            Semaphore.Release();
        }
    }

    public bool ContinueWorking { get; set; }
    public SemaphoreSlim Semaphore { get; }
}
