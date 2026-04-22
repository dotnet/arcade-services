// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Caching;

namespace Maestro.Common;

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

public class MemoryCache : ICache, IDisposable
{
    private readonly System.Runtime.Caching.MemoryCache _cache = new("Darc");

    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var options = new CacheItemPolicy();
        if (expiration.HasValue)
        {
            options.AbsoluteExpiration = DateTimeOffset.Now.Add(expiration.Value);
        }

        _cache.Set(key, value, options);
        return Task.FromResult(true);
    }

    public Task<T?> TryGetAsync<T>(string key) where T : class
    {
        var value = _cache.Get(key) as T;
        return Task.FromResult(value);
    }

    public Task<bool> DeleteAsync(string key)
    {
        bool exists = _cache.Contains(key);
        if (exists)
        {
            _cache.Remove(key);
        }

        return Task.FromResult(exists);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
