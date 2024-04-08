// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Queue;

internal class JobScopeManager
{
    private readonly AutoResetEvent _autoResetEvent;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobScopeManager> _logger;
    private JobsProcessorState _state;

    public JobsProcessorState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                _logger.LogInformation($"JobsProcessor state changing to {value}");
            }
        }
    }

    public JobScopeManager(bool initializingOnStartup, IServiceProvider serviceProvider, ILogger<JobScopeManager> logger)
    {
        _autoResetEvent = new AutoResetEvent(!initializingOnStartup);
        _logger = logger;
        _serviceProvider = serviceProvider;
        _state = initializingOnStartup ? JobsProcessorState.Initializing : JobsProcessorState.Working;
    }

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
        var scope = _serviceProvider.CreateScope();
        return ActivatorUtilities.CreateInstance<JobScope>(scope.ServiceProvider, scope, new Action(JobFinished));
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

    public void InitializingDone()
    {
        if (State == JobsProcessorState.Initializing)
        {
            State = JobsProcessorState.Stopped;
        }
    }
}
