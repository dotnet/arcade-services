// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

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

public class MemoryCache : ICache, IDisposable
{
    private readonly Extensions.Caching.Memory.MemoryCache _cache = new(new MemoryCacheOptions());

    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        var options = new MemoryCacheEntryOptions();
        if (expiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = expiration.Value;
        }

        _cache.Set(key, value, options);
        return Task.FromResult(true);
    }

    public Task<T?> TryGetAsync<T>(string key) where T : class
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task<bool> DeleteAsync(string key)
    {
        bool exists = _cache.TryGetValue(key, out _);
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
