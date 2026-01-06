// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Common;


public interface IDistributedLock
{
    /// <summary>
    /// Executes the specified action within a distributed lock, and return the result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the action.</typeparam>
    /// <param name="key">The unique identifier for the lock.</param>
    /// <param name="action">The asynchronous delegate to execute while the lock is held. Cannot be null.</param>
    /// <param name="timeout">The maximum duration to wait to acquire the lock before the operation is aborted, or null to use the default
    /// timeout.</param>
    /// <param name="cancellationToken">A token to observe while waiting to acquire the lock and executing the action. The operation is canceled if the
    /// token is signaled.</param>
    /// <returns>A task that represents the asynchronous operation. The task completes when the action has finished executing
    /// under the lock.</returns>
    Task<T> ExecuteWithLockAsync<T>(
        string key,
        Func<Task<T>> action,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the specified action within a distributed lock.
    /// </summary>
    /// <param name="key">The unique identifier for the lock. Actions using the same key are executed sequentially to ensure mutual
    /// exclusion.</param>
    /// <param name="action">The asynchronous delegate to execute while the lock is held. Cannot be null.</param>
    /// <param name="timeout">The maximum duration to wait to acquire the lock before the operation is aborted, or null to use the default
    /// timeout.</param>
    /// <param name="cancellationToken">A token to observe while waiting to acquire the lock and executing the action. The operation is canceled if the
    /// token is signaled.</param>
    /// <returns>A task that represents the asynchronous operation. The task completes when the action has finished executing
    /// under the lock.</returns>
    Task ExecuteWithLockAsync(
        string key,
        Func<Task> action,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public class DistributedLock(
    IRedisCacheFactory redisCacheFactory,
    ILogger<DistributedLock> logger) : IDistributedLock
{
    private readonly IRedisCacheFactory _cacheFactory = redisCacheFactory;
    private readonly ILogger<DistributedLock> _logger = logger;

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
            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformation(
                "Attempting to acquire distributed lock with key {LockKey}",
                key);

            @lock = await _cacheFactory.TryAcquireLock(
                key,
                effectiveTimeout,
                cancellationToken: cancellationToken);

            if (@lock == null)
            {
                _logger.LogWarning(
                    "Failed to acquire distributed lock with key {LockKey} after {WaitMs} ms.",
                    key,
                    stopwatch.ElapsedMilliseconds);
                
                continue;
            }

            await using (@lock)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Acquired distributed lock for key '{Key}' after {ElapsedMilliseconds} ms",
                    key, stopwatch.ElapsedMilliseconds);
                try
                {
                    return await action();
                }
                finally
                {
                    _logger.LogInformation("Released distributed lock for key '{Key}'", key);
                }
            }
        } while (@lock == null);

        // This can never be reached, but the compiler doesn't know that so we need to throw.
        throw new InvalidOperationException("Failed to acquire lock.");
    }

    /// <summary>
    /// Non-generic overload for actions that do not return a value.
    /// </summary>
    public async Task ExecuteWithLockAsync(
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

