// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProductConstructionService.WorkItems;

public class WorkItemScopeManager
{
    private readonly AutoResetEvent _autoResetEvent;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WorkItemScopeManager> _logger;
    private WorkItemProcessorState _state;

    public WorkItemProcessorState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                _logger.LogInformation("WorkItemsProcessor state changing to {newValue}", value);
            }
        }
    }

    internal WorkItemScopeManager(
        bool initializingOnStartup,
        IServiceProvider serviceProvider,
        ILogger<WorkItemScopeManager> logger)
    {
        _autoResetEvent = new AutoResetEvent(!initializingOnStartup);
        _logger = logger;
        _serviceProvider = serviceProvider;
        _state = initializingOnStartup ? WorkItemProcessorState.Initializing : WorkItemProcessorState.Working;
    }

    public void Start()
    {
        State = WorkItemProcessorState.Working;
        _autoResetEvent.Set();
    }

    /// <summary>
    /// Creates a new scope for the currently executing WorkItem, when the the WorkItemsProcessor is in the `Working` state.
    /// </summary>
    public WorkItemScope BeginWorkItemScopeWhenReady()
    {
        _autoResetEvent.WaitOne();
        var scope = _serviceProvider.CreateScope();
        return new WorkItemScope(
            scope.ServiceProvider.GetRequiredService<IOptions<WorkItemProcessorRegistrations>>(),
            new Action(WorkItemFinished),
            scope,
            scope.ServiceProvider.GetRequiredService<ITelemetryRecorder>());
    }

    private void WorkItemFinished()
    {
        switch (State)
        {
            case WorkItemProcessorState.Stopping:
                State = WorkItemProcessorState.Stopped;
                break;
            case WorkItemProcessorState.Working:
                _autoResetEvent.Set();
                break;
        }
    }

    public void FinishWorkItemAndStop()
    {
        if (State == WorkItemProcessorState.Working)
        {
            State = WorkItemProcessorState.Stopping;
        }
    }

    public void InitializingDone()
    {
        if (State == WorkItemProcessorState.Initializing)
        {
            State = WorkItemProcessorState.Stopped;
        }
    }
}
