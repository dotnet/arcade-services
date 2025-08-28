// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Generic interface for caching POCOs. Implementations should create keys based on the provided key and
/// object type to avoid collisions.
/// </summary>
public interface IDistributedCacheClient
{
    Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    Task<T?> TryGetAsync<T>(string key) where T : class;
    Task<bool> DeleteAsync(string key);
}

/// <summary>
/// This no-op cache client the default implementation of IDistributedCacheClient in DarcLib. Caching is not mandatory
/// and requires explicit implemnetation if used.
/// </summary>
public class NoOpCacheClient : IDistributedCacheClient
{
    public Task<bool> TrySetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        return Task.FromResult(false);
    }
    public Task<T?> TryGetAsync<T>(string key) where T : class
    {
        return Task.FromResult<T?>(null);
    }
    public Task<bool> DeleteAsync(string key)
    {
        return Task.FromResult(false);
    }
}
