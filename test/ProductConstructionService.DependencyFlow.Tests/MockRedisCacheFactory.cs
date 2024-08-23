// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.DependencyFlow.Tests;

internal class MockRedisCacheFactory : IRedisCacheFactory
{
    private readonly Dictionary<string, object> _data = [];

    public IReadOnlyDictionary<string, object> Data => _data;

    public IRedisCache Create(string key)
    {
        throw new NotImplementedException();
    }

    public IRedisCache<T> Create<T>(string key) where T : class
    {
        return new MockRedisCache<T>(key, _data);
    }
}
