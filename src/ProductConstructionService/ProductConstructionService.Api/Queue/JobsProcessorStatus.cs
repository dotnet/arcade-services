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
    public JobsProcessorStatus(ManualResetEventSlim manualResetEvent)
    {
        State = JobsProcessorState.Working;
        _manualResetEvent = manualResetEvent;
    }

    public JobsProcessorState State { get; private set; }
    private readonly ManualResetEventSlim _manualResetEvent;

    public void Start()
    {
        State = JobsProcessorState.Working;
        _manualResetEvent.Set();
    }

    public void WaitIfStopping(CancellationToken cancellationToken)
    {
        if (!_manualResetEvent.IsSet)
        {
            State = JobsProcessorState.StoppedWorking;
        }
        _manualResetEvent.Wait(cancellationToken);
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
