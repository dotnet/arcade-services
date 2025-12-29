// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Common;

public class DistributedLock
{
    private readonly IRedisCacheFactory _cacheFactory;

    public DistributedLock(IRedisCacheFactory cacheFactory)
    {
        _cacheFactory = cacheFactory;
    }

    public async Task<T> ExecuteWithLockAsync<T>(
        string key,
        Func<Task<T>> action,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromHours(1);
        IAsyncDisposable? @lock;

        do
        {
            await using (@lock = await _cacheFactory.TryAcquireLock(key, effectiveTimeout, cancellationToken: cancellationToken))
            {
                if (@lock == null)
                {
                    continue; // retry
                }

                return await action(); // run the action and return its result
            }
        } while (@lock == null);

        // This is only reached if somehow @lock is never acquired, which theoretically can't happen
        throw new InvalidOperationException("Failed to acquire lock.");
    }

    /// <summary>
    /// Non-generic overload for actions that do not return a value.
    /// </summary>
    public async Task RunWithLockAsync(
    string key,
    Func<Task> action,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default)
    {
        await ExecuteWithLockAsync<object>(
            key,
            async () =>
            {
                await action();
                return null!;
            },
            timeout: timeout,
            cancellationToken: cancellationToken);
    }
}

