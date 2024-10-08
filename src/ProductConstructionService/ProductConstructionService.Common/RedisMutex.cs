// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace ProductConstructionService.Common;

public interface IRedisMutex
{
    public Task<T> DoWhenReady<T>(string mutexName, Func<Task<T>> action);
}

/// <summary>
/// Mutex implemented using a redis cache meant for synchronization between repplicas
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

    public async Task<T> DoWhenReady<T>(string mutexName, Func<Task<T>> action)
    {
        IRedisCache mutexCache = _cacheFactory.Create($"{mutexName}_mutex");

        try
        {
            // Check if a different replica is processing a subscription that belongs to the same batch
            string? state;
            do
            {
                state = await mutexCache.GetAsync();
            } while (await Utility.SleepIfTrue(
                () => !string.IsNullOrEmpty(state),
                MutexWakeUpTimeSeconds,
                () => _logger.LogInformation("Waiting for mutex {mutexName} mutexName to become available", mutexName)));
            // Updating assets should never take more than an hour. If it does, it's possible something
            // bad happened, so reset the mutex
            _logger.LogInformation("Taking mutex {mutexName}", mutexName);
            await mutexCache.SetAsync("busy", TimeSpan.FromHours(1));

            return await action();
        }
        finally
        {
            _logger.LogInformation("Releasing mutex {mutexName}", mutexName);
            // if something happens, we don't want this subscription to be blocked forever, reset the mutex
            await mutexCache.TryDeleteAsync();
        }
    }
}
