// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.Utilities.Parallel;

public readonly struct SemaphoreLock : IDisposable
{
    private readonly SemaphoreSlim _sem;

    private SemaphoreLock(SemaphoreSlim sem)
    {
        _sem = sem;
    }

    public void Dispose()
    {
        _sem.Release();
    }

    public static ValueTask<SemaphoreLock> LockAsync(SemaphoreSlim sem)
    {
        Task waitTask = sem.WaitAsync();
        if (waitTask.IsCompletedSuccessfully)
        {
            return new ValueTask<SemaphoreLock>(new SemaphoreLock(sem));
        }

        static async Task<SemaphoreLock> WaitForLock(Task waitTask, SemaphoreSlim sem)
        {
            await waitTask.ConfigureAwait(false);
            return new SemaphoreLock(sem);
        }

        return new ValueTask<SemaphoreLock>(WaitForLock(waitTask, sem));
    }
}
