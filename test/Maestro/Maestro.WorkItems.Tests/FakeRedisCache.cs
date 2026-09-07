// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Maestro.Services.Common.Cache;

namespace Maestro.WorkItem.Tests;

internal class FakeRedisCache : IRedisCache
{
    private readonly ConcurrentDictionary<string, string> _store;
    private readonly string _key;

    public FakeRedisCache()
        : this(new ConcurrentDictionary<string, string>(), "key")
    {
    }

    public FakeRedisCache(ConcurrentDictionary<string, string> store, string key)
    {
        _store = store;
        _key = key;
    }

    public Task SetAsync(string value, TimeSpan? expiration = null)
    {
        _store[_key] = value;
        return Task.CompletedTask;
    }

    public Task<bool> TryDeleteAsync() => Task.FromResult(_store.TryRemove(_key, out _));
    public Task<string?> TryGetAsync() => Task.FromResult(_store.TryGetValue(_key, out var value) ? value : null);
    public Task<string?> GetAsync() => TryGetAsync();
    public Task<string?> GetAsync(string key) => Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);
    public IAsyncEnumerable<string> GetKeysAsync(string pattern) => throw new NotImplementedException();
    Task<Dictionary<string, string?>> IRedisCache.GetBatchAsync(IEnumerable<string> keys) => throw new NotImplementedException();
}

internal class FakeRedisCacheFactory : IRedisCacheFactory
{
    public ConcurrentDictionary<string, string> Store { get; } = new();

    public IRedisCache Create(string stateKey) => new FakeRedisCache(Store, stateKey);

    public IRedisCache<T> Create<T>(string stateKey, bool includeTypeInKey = true) where T : class
        => throw new NotImplementedException();

    public Task<IAsyncDisposable?> TryAcquireLock(string lockKey, TimeSpan expiration, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
