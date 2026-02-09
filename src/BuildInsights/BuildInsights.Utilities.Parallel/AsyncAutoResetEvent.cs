// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

/// <summary>
/// It's like "AutoResetEvent", except it has "WaitAsync" instead of the Thread killing "Wait"
/// </summary>
public class AsyncAutoResetEvent
{
    private static readonly Task s_completed = Task.FromResult(true);

    private readonly Queue<Registration> _waits = new();
    private bool _signaled;

    private class Registration
    {
        public readonly TaskCompletionSource<bool> TaskCompletionSource;
        public readonly CancellationTokenRegistration CancellationRegistration;

        public Registration(TaskCompletionSource<bool> taskCompletionSource, CancellationTokenRegistration cancellationRegistration)
        {
            TaskCompletionSource = taskCompletionSource;
            CancellationRegistration = cancellationRegistration;
        }
    }

    public Task WaitAsync(CancellationToken token = default)
    {
        lock (_waits)
        {
            if (_signaled)
            {
                _signaled = false;
                return s_completed;
            }

            var tcs = new TaskCompletionSource<bool>();
            if (token.CanBeCanceled)
            {
                CancellationTokenRegistration registration = token.Register(CancelRegistration, tcs);

                _waits.Enqueue(new Registration(tcs, registration));
            }
            else
            {
                _waits.Enqueue(new Registration(tcs, default));
            }

            return tcs.Task;
        }
    }

    private void CancelRegistration(object t)
    {
        lock (_waits)
        {
            ((TaskCompletionSource<bool>)t).TrySetCanceled();
        }
    }

    public void Set()
    {
        Registration toRelease = null;

        lock (_waits)
        {
            while (toRelease == null)
            {
                if (_waits.Count > 0)
                {
                    toRelease = _waits.Dequeue();
                    if (toRelease.TaskCompletionSource.Task.IsCanceled)
                    {
                        toRelease = null;
                    }
                }
                else
                {
                    _signaled = true;
                    return;
                }
            }

            toRelease.CancellationRegistration.Dispose();
        }

        toRelease.TaskCompletionSource.SetResult(true);
    }
}
