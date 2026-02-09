// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

/// <summary>
/// It's like "ManualResetEvent", except it has "WaitAsync" instead of the Thread killing "Wait"
/// </summary>
public class AsyncManualResetEvent
{
    private static readonly Task s_completed = Task.FromResult(true);

    private readonly Queue<Registration> _waits = new Queue<Registration>();
    private bool _signaled;

    private class Registration
    {
        public readonly TaskCompletionSource<bool> TaskCompletionSource;
        public readonly CancellationTokenRegistration? CancellationRegistration;

        public Registration(TaskCompletionSource<bool> taskCompletionSource, CancellationTokenRegistration? cancellationRegistration)
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
                _waits.Enqueue(new Registration(tcs, null));
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

    public void Reset()
    {
        _signaled = false;
    }

    public void Set()
    {
        lock (_waits)
        {
            foreach (var item in _waits)
            {
                item.TaskCompletionSource.TrySetResult(true);
                // ReSharper disable once ImpureMethodCallOnReadonlyValueField
                // This is a struct wrapping a class, and the operations are performed on the class, so it's safe
                item.CancellationRegistration?.Dispose();
            }
            _signaled = true;
        }
    }
}
