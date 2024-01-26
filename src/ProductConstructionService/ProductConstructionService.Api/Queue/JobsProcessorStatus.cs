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
        _manualResetEvent = new(true);
    }

    public JobsProcessorState State { get; private set; }
    private readonly ManualResetEventSlim _manualResetEvent;

    public void Reset()
    {
        State = JobsProcessorState.Working;
        _manualResetEvent.Set();
    }

    public void WaitIfStoppingAsync(CancellationToken cancellationToken)
    {
        if (!_manualResetEvent.IsSet)
        {
            State = JobsProcessorState.StoppedWorking;
        }
        _manualResetEvent.Wait();
    }

    public void FinishJobAndStop()
    {
        if (State == JobsProcessorState.Working)
        {
            State = JobsProcessorState.FinishingJobAndStopping;
        }
        _manualResetEvent.Reset();
    }

}
