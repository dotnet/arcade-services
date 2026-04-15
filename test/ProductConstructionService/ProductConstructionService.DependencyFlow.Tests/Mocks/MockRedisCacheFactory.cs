// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Moq;
using ProductConstructionService.Common.Cache;

namespace ProductConstructionService.DependencyFlow.Tests.Mocks;

internal class MockRedisCacheFactory : IRedisCacheFactory
{
    public Dictionary<string, object> Data { get; } = [];

    public IRedisCache Create(string key)
    {
        return new MockRedisCache(key, Data);
    }

    public IRedisCache<T> Create<T>(string key, bool includeTypeInKey = true) where T : class
    {
        return new MockRedisCache<T>(key, Data);
    }

    public Task<IAsyncDisposable?> TryAcquireLock(string lockKey, TimeSpan expiration, CancellationToken cancellationToken = default)
        => Task.FromResult(Mock.Of<IAsyncDisposable?>());
}
