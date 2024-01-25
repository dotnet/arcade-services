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
        _semaphore = new(1);
    }

    public JobsProcessorState State { get; set; }
    private readonly SemaphoreSlim _semaphore;

    public void Reset()
    {
        State = JobsProcessorState.Working;
        if (_semaphore.CurrentCount == 0)
        {
            _semaphore.Release();
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
    }
}
