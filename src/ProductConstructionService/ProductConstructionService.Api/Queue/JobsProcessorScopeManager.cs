// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Metrics;
using ProductConstructionService.Api.Metrics;

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
    public JobsProcessorScopeManager(bool workOnStartup, IServiceProvider serviceProvider)
    {
        _autoResetEvent = new AutoResetEvent(workOnStartup);
        State = workOnStartup ? JobsProcessorState.Working : JobsProcessorState.Stopped;
        _serviceProvider = serviceProvider;
    }

    public JobsProcessorState State { get; private set; }
    private readonly AutoResetEvent _autoResetEvent;
    private readonly IServiceProvider _serviceProvider;

    public void Start()
    {
        State = JobsProcessorState.Working;
        _autoResetEvent.Set();
    }

    /// <summary>
    /// Creates a new scope for the currently executing Job, when the the JobsProcessor is in the `Working` state.
    /// </summary>
    public JobScope BeginJobScopeWhenReady()
    {
        _autoResetEvent.WaitOne();
        return new JobScope(this, _serviceProvider.CreateScope());
    }

    public void JobFinished()
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

    
}
