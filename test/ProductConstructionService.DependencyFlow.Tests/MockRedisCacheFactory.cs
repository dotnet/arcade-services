﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.DependencyFlow.Tests;

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
}
