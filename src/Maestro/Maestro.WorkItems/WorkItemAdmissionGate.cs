// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.WorkItems;

/// <summary>
/// In-process gate deciding whether this replica may admit new queue work.
/// The admission check and the active count increment happen under one lock so that
/// no consumer can enter after a stop decision was made.
/// </summary>
public sealed class WorkItemAdmissionGate
{
    private readonly Lock _syncRoot = new();

    private bool _isOpen;
    private int _activeAdmissions;
    private TaskCompletionSource _openedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _drainedSource = CreateCompletedSource();

    public bool IsOpen
    {
        get
        {
            lock (_syncRoot)
            {
                return _isOpen;
            }
        }
    }

    public int ActiveAdmissionCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeAdmissions;
            }
        }
    }

    public void Open()
    {
        lock (_syncRoot)
        {
            _isOpen = true;
            _openedSource.TrySetResult();
        }
    }

    public void Close()
    {
        lock (_syncRoot)
        {
            _isOpen = false;
            if (_openedSource.Task.IsCompleted)
            {
                _openedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    /// <summary>
    /// Waits until the gate is open and then takes an admission lease. The lease is held for the
    /// whole consumer cycle, including an empty queue poll delay, so that a stopped replica cannot
    /// hold a newly dequeued invisible message.
    /// </summary>
    public async Task<WorkItemAdmissionLease> AdmitWhenOpenAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task opened;
            lock (_syncRoot)
            {
                if (_isOpen)
                {
                    if (_activeAdmissions++ == 0)
                    {
                        _drainedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    return new WorkItemAdmissionLease(this);
                }

                opened = _openedSource.Task;
            }

            await opened.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Completes once every admitted consumer cycle has finished. Callers are expected to close
    /// the gate first, otherwise new admissions keep arriving.
    /// </summary>
    public Task WaitUntilDrainedAsync(CancellationToken cancellationToken)
    {
        Task drained;
        lock (_syncRoot)
        {
            drained = _drainedSource.Task;
        }

        return drained.WaitAsync(cancellationToken);
    }

    internal void ReleaseAdmission()
    {
        lock (_syncRoot)
        {
            if (--_activeAdmissions == 0)
            {
                _drainedSource.TrySetResult();
            }
        }
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

public sealed class WorkItemAdmissionLease : IDisposable
{
    private WorkItemAdmissionGate? _gate;

    internal WorkItemAdmissionLease(WorkItemAdmissionGate gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        WorkItemAdmissionGate? gate = Interlocked.Exchange(ref _gate, null);
        gate?.ReleaseAdmission();
    }
}
