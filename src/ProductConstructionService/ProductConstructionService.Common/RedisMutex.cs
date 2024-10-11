// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Common;

public interface IRedisMutex
{
    public Task<T> EnterWhenAvailable<T>(string mutexName, Func<Task<T>> action, TimeSpan lockTime = default);
}

/// <summary>
/// Mutex implemented using a redis cache meant for synchronization between replicas
/// </summary>
public class RedisMutex : IRedisMutex
{
    private readonly IRedisCacheFactory _cacheFactory;
    private readonly ILogger<RedisMutex> _logger;

    private const int MutexWakeUpTimeSeconds = 5;

    public RedisMutex(IRedisCacheFactory cacheFactory, ILogger<RedisMutex> logger)
    {
        _cacheFactory = cacheFactory;
        _logger = logger;
    }

    public async Task<T> EnterWhenAvailable<T>(
        string mutexName,
        Func<Task<T>> action,
        TimeSpan lockTime = default)
    {
        if (lockTime == default)
        {
            lockTime = TimeSpan.FromHours(1);
        }

        IRedisCache mutexCache = _cacheFactory.Create($"Mutex_{mutexName}");

        try
        {
            // Check if someone else has already taken the mutex
            string? state;
            do
            {
                state = await mutexCache.GetAsync();
            } while (await Utility.SleepIfTrue(
                () => !string.IsNullOrEmpty(state),
                MutexWakeUpTimeSeconds,
                () => _logger.LogInformation("Waiting for mutex {mutexName} mutexName to become available", mutexName)));
            _logger.LogInformation("Taking mutex {mutexName}", mutexName);

            // If for whatever reason we get stuck in action, we don't want the mutex to lock forever
            // It will release the lock after lockTime
            await mutexCache.SetAsync("busy", lockTime);

            return await action();
        }
        finally
        {
            // When we're done, or get an exception, release the mutex and let others try
            _logger.LogInformation("Releasing mutex {mutexName}", mutexName);
            await mutexCache.TryDeleteAsync();
        }
    }
}
