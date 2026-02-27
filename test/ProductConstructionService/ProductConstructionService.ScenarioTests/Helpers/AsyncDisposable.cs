// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.ScenarioTests.Helpers;

public class AsyncDisposable : IAsyncDisposable
{
    private Func<ValueTask> _dispose;

    public static IAsyncDisposable Create(Func<ValueTask> dispose)
    {
        return new AsyncDisposable(dispose);
    }

    private AsyncDisposable(Func<ValueTask> dispose)
    {
        _dispose = dispose;
    }

    public ValueTask DisposeAsync()
    {
        Func<ValueTask> dispose = Interlocked.Exchange(ref _dispose, null);
        return dispose?.Invoke() ?? new ValueTask(Task.CompletedTask);
    }
}
