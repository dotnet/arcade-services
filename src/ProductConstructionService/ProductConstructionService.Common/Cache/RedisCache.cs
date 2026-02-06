// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ProductConstructionService.Common.Cache;

public interface IRedisCache
{
    Task<string?> GetAsync(string key);
    Task<string?> GetAsync();
    Task SetAsync(string value, TimeSpan? expiration = null);
    Task<bool> TryDeleteAsync();
    Task<string?> TryGetAsync();
    IAsyncEnumerable<string> GetKeysAsync(string pattern);
}

public class RedisCache : IRedisCache
{
    internal static readonly TimeSpan DefaultExpiration = TimeSpan.FromDays(180);

    private readonly string _stateKey;
    private readonly IConnectionMultiplexer _connection;

    private IDatabase Cache => _connection.GetDatabase();

    public RedisCache(IConnectionMultiplexer connection, string stateKey)
    {
        _connection = connection;
        _stateKey = stateKey;
    }

    public async Task SetAsync(string value, TimeSpan? expiration = null)
    {
        await Cache.StringSetAsync(_stateKey, value, expiration ?? DefaultExpiration);
    }

    public async Task<string?> TryGetAsync()
    {
        var value = await Cache.StringGetAsync(_stateKey);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> TryDeleteAsync()
    {
        return await Cache.KeyDeleteAsync(_stateKey);
    }

    public async Task<string?> GetAsync()
    {
        return await Cache.StringGetAsync(_stateKey);
    }

    public async Task<string?> GetAsync(string key)
    {
        return await Cache.StringGetAsync(key);
    }

    public async IAsyncEnumerable<string> GetKeysAsync(string pattern)
    {
        // We most likely only have one endpoint so no need to parallelize this part
        foreach (var endpoint in _connection.GetEndPoints())
        {
            var server = _connection.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                yield return key.ToString();
            }
        }
    }
}

public interface IRedisCache<T> where T : class
{
    Task SetAsync(T value, TimeSpan? expiration = null);
    Task<T?> TryDeleteAsync();
    Task<T?> TryGetStateAsync();
    IAsyncEnumerable<string> GetKeysAsync(string pattern);
}

public class RedisCache<T> : IRedisCache<T> where T : class
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IRedisCache _cache;
    private readonly ILogger<RedisCache> _logger;

    public RedisCache(IRedisCache cache, ILogger<RedisCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public IAsyncEnumerable<string> GetKeysAsync(string pattern)
        => _cache.GetKeysAsync(pattern);

    public async Task<T?> TryGetStateAsync()
        => await TryGetStateAsync(false);

    public async Task<T?> TryDeleteAsync()
        => await TryGetStateAsync(true);

    public async Task SetAsync(T value, TimeSpan? expiration = null)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(value, JsonSerializerOptions);
        }
        catch (SerializationException e)
        {
            _logger.LogError(e, "Failed to serialize {type} into cache", typeof(T).Name);
            return;
        }

        await _cache.SetAsync(json, expiration ?? RedisCache.DefaultExpiration);
    }

    private async Task<T?> TryGetStateAsync(bool delete)
    {
        var state = await _cache.TryGetAsync();
        if (state == null)
        {
            return null;
        }

        if (delete)
        {
            await _cache.TryDeleteAsync();
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(state, JsonSerializerOptions);
            return result;
        }
        catch (SerializationException e)
        {
            // If we can't deserialize (maybe the model changed?), we drop the state
            _logger.LogError(e, "Failed to deserialize state {type}. Removing from state memory. Original value: {value}",
                typeof(T).Name,
                await TryDeleteAsync());
            return null;
        }
    }
}
