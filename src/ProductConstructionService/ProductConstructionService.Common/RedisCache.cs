// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface IRedisCache
{
    Task<string?> GetAsync();
    Task SetAsync(string value, TimeSpan? expiration = null);
    Task<string?> TryDeleteAsync();
    Task<string?> TryGetAsync();
}

public class RedisCache : IRedisCache
{
    internal static readonly TimeSpan DefaultExpiration = TimeSpan.FromDays(90);

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

    public async Task<string?> TryDeleteAsync()
    {
        return await Cache.StringGetDeleteAsync(_stateKey);
    }

    public async Task<string?> GetAsync()
    {
        return await Cache.StringGetAsync(_stateKey);
    }
}

public interface IRedisCache<T> where T : class
{
    Task SetAsync(T value, TimeSpan? expiration = null);
    Task<T?> TryDeleteAsync();
    Task<T?> TryGetStateAsync();
}

public class RedisCache<T> : IRedisCache<T> where T : class
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IRedisCache _stateManager;
    private readonly ILogger<RedisCache> _logger;

    public RedisCache(
        IRedisCache stateManager,
        ILogger<RedisCache> logger)
    {
        _stateManager = stateManager;
        _logger = logger;
    }

    public async Task<T?> TryGetStateAsync() => await TryGetStateAsync(false);

    public async Task<T?> TryDeleteAsync() => await TryGetStateAsync(true);

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

        await _stateManager.SetAsync(json, expiration ?? RedisCache.DefaultExpiration);
    }

    private async Task<T?> TryGetStateAsync(bool delete)
    {
        var state = delete
            ? await _stateManager.TryDeleteAsync()
            : await _stateManager.TryGetAsync();
        if (state == null)
        {
            return null;
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
