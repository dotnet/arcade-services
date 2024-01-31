// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

public enum JobsProcessorState
{
    // The JobsProcessor will keep taking and processing new jobs
    Working,
    // The JobsProcessor isn't doing anything
    Stopped,
    // The JobsProcessor will finish its current job and stop
    Stopping
}

public class JobsProcessorScopeManager
{
    public JobsProcessorState State { get; private set; } = JobsProcessorState.Stopped;
    private readonly AutoResetEvent _autoResetEvent = new(false);

    public void Start()
    {
        State = JobsProcessorState.Working;
        _autoResetEvent.Set();
    }

    /// <summary>
    /// Creates a new scope for the currently executing Job, when the the JobsProcessor is in the `Working` state.
    /// </summary>
    public IDisposable BeginJobScopeWhenReady(CancellationToken cancellationToken)
    {
        _autoResetEvent.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
        return new JobScope(this);
    }

    private void JobFinished()
    {
        switch (State)
        {
            case JobsProcessorState.Stopping:
                State = JobsProcessorState.Stopped;
                break;
            case JobsProcessorState.Working:
                _autoResetEvent.Set();
                break;
        }
    }

    public void FinishJobAndStop()
    {
        if (State == JobsProcessorState.Working)
        {
            State = JobsProcessorState.Stopping;
        }
    }

    private class JobScope(JobsProcessorScopeManager status) : IDisposable
    {
        public void Dispose() => status.JobFinished();
    }
}
