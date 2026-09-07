// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maestro.WorkItems;

public sealed record WorkItemProcessorStateControllerOptions(bool WaitForInitialization, TimeSpan PollingInterval);

/// <summary>
/// Replica side of the desired/observed protocol. The replica starts stopped, opens queue admission
/// only while its desired state is <see cref="WorkItemProcessorState.Working"/>, and acknowledges a stop
/// only after every admitted consumer cycle has finished.
/// </summary>
public sealed class WorkItemProcessorStateController : BackgroundService
{
    private readonly IReplicaWorkItemProcessorStateStore _stateStore;
    private readonly WorkItemAdmissionGate _admissionGate;
    private readonly WorkItemProcessorStateControllerOptions _options;
    private readonly ILogger<WorkItemProcessorStateController> _logger;
    private readonly TaskCompletionSource _initializationCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WorkItemProcessorState? _reportedState;

    public WorkItemProcessorStateController(
        IReplicaWorkItemProcessorStateStore stateStore,
        WorkItemAdmissionGate admissionGate,
        WorkItemProcessorStateControllerOptions options,
        ILogger<WorkItemProcessorStateController> logger)
    {
        _stateStore = stateStore;
        _admissionGate = admissionGate;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// True while a locally required initialization keeps admission closed even though the replica
    /// may already be desired to work. This is an in-process prerequisite, never a distributed state.
    /// </summary>
    public bool IsInitializationPending => _options.WaitForInitialization && !_initializationCompleted.Task.IsCompleted;

    public WorkItemProcessorState ObservedState => _reportedState ?? WorkItemProcessorState.Stopped;

    public void InitializationFinished()
    {
        if (_initializationCompleted.TrySetResult())
        {
            _logger.LogInformation("Local initialization finished, the replica can start accepting work items");
        }
    }

    /// <summary>
    /// Closes admission and reports the replica as stopped. Every replica starts in this state.
    /// </summary>
    public async Task ReportStartupStateAsync(CancellationToken cancellationToken)
    {
        _admissionGate.Close();
        await ReportObservedStateAsync(WorkItemProcessorState.Stopped, cancellationToken);
    }

    public async Task ApplyDesiredStateAsync(CancellationToken cancellationToken)
    {
        WorkItemProcessorState? desiredState = await _stateStore.GetDesiredStateAsync(cancellationToken);

        if (desiredState == null)
        {
            _logger.LogDebug("No desired queue processing state is set, keeping the current local state");
        }
        else if (desiredState == WorkItemProcessorState.Working)
        {
            await StartWorkingAsync(cancellationToken);
        }
        else
        {
            await StopWorkingAsync(cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // We yield so that the rest of the service can progress initialization
        await Task.Yield();

        try
        {
            await ReportStartupStateAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ApplyDesiredStateAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to apply the desired queue processing state, keeping the current local state");
                }

                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Queue processing state controller is shutting down");
        }
    }

    private async Task StartWorkingAsync(CancellationToken cancellationToken)
    {
        if (IsInitializationPending)
        {
            _logger.LogInformation("Queue processing is desired but local initialization is still pending, admission stays closed");
            return;
        }

        if (_admissionGate.IsOpen && _reportedState == WorkItemProcessorState.Working)
        {
            return;
        }

        // Acknowledging the start before opening admission guarantees that a replica never processes
        // work items the control plane doesn't know about.
        await ReportObservedStateAsync(WorkItemProcessorState.Working, cancellationToken);

        WorkItemProcessorState? confirmedState = await _stateStore.GetDesiredStateAsync(cancellationToken);
        if (confirmedState == WorkItemProcessorState.Working)
        {
            _admissionGate.Open();
            _logger.LogInformation("Queue admission is open");
        }
        else if (confirmedState == WorkItemProcessorState.Stopped)
        {
            await StopWorkingAsync(cancellationToken);
        }
    }

    private async Task StopWorkingAsync(CancellationToken cancellationToken)
    {
        _admissionGate.Close();
        await _admissionGate.WaitUntilDrainedAsync(cancellationToken);
        await ReportObservedStateAsync(WorkItemProcessorState.Stopped, cancellationToken);
    }

    private async Task ReportObservedStateAsync(WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        if (_reportedState == state)
        {
            return;
        }

        await _stateStore.SetObservedStateAsync(state, cancellationToken);
        _reportedState = state;
    }
}
