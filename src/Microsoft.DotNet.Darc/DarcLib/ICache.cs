// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface ICache
{
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    Task<T?> TryGetAsync<T>(string key) where T : class;
    Task<bool> DeleteAsync(string key);
}

public class NoopCache : ICache
{
    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        return Task.FromResult(true);
    }
    public Task<T?> TryGetAsync<T>(string key) where T : class
    {
        return Task.FromResult<T?>(null);
    }
    public Task<bool> DeleteAsync(string key)
    {
        return Task.FromResult(true);
    }
}

public class MemoryCache : ICache
{
    private readonly ConcurrentDictionary<string, (object Value, DateTime? Expiration)> _cache = new();
    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var expirationTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : (DateTime?)null;
        _cache[key] = (value, expirationTime);
        return Task.FromResult(true);
    }

    public Task<T?> TryGetAsync<T>(string key) where T : class
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.Expiration.HasValue || entry.Expiration > DateTime.UtcNow)
            {
                return Task.FromResult((T?)entry.Value);
            }
            else
            {
                _cache.TryRemove(key, out _);
            }
        }
        return Task.FromResult<T?>(null);
    }

    public Task<bool> DeleteAsync(string key)
    {
        return Task.FromResult(_cache.TryRemove(key, out _));
    }
}
