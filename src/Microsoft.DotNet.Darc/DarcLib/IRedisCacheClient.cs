// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;
public interface IRedisCacheClient
{
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    Task<T> TryGetAsync<T>(string key) where T : class;
    Task<bool> DeleteAsync(string key);
}

// This no-op redis client is used when DarcLib is invoked through CLI operations where redis is not available.
public class NoOpRedisClient : IRedisCacheClient
{
    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        return Task.FromResult(false);
    }
    public Task<T> TryGetAsync<T>(string key) where T : class
    {
        return Task.FromResult<T>(null);
    }
    public Task<bool> DeleteAsync(string key)
    {
        return Task.FromResult(false);
    }
}
