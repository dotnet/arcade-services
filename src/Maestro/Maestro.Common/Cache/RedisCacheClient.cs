// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace Maestro.Common.Cache;

public interface IRedisCacheClient
{
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    Task<T?> TryGetAsync<T>(string key) where T : class;
    Task<bool> DeleteAsync(string key);
}

/// <summary>
/// This class acts as a delegate for RedisCache and RedisCacheFactory.
/// It is needed because DarcLib does not depend on ProductConstructionService and can only access caching through a delegate.
/// </summary>
public class RedisCacheClient : IRedisCacheClient
{
    private readonly IRedisCacheFactory _factory;
    private readonly ILogger<RedisCacheClient> _logger;

    public RedisCacheClient(IRedisCacheFactory factory, ILogger<RedisCacheClient> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<T?> TryGetAsync<T>(string key) where T : class
    {
        return await _factory.Create<T>(key).TryGetStateAsync();
    }

    public async Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        try
        {
            await _factory.Create<T>(key).SetAsync(value, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set value in Redis cache for key {Key}", key);
            return false;
        }
        return true;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        return await _factory.Create(key).TryDeleteAsync();
    }
}
