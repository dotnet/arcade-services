﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using ProductConstructionService.Common;

namespace ProductConstructionService.DependencyFlow.Tests;

internal class MockRedisCache : IRedisCache
{
    private readonly Dictionary<string, object> _data = [];
    private readonly string _key;

    public IReadOnlyDictionary<string, object> Data => _data;

    public MockRedisCache(string key, Dictionary<string, object> data)
    {
        _key = key;
        _data = data;
    }

    public Task<string?> GetAsync()
    {
        return _data.TryGetValue(_key, out object? value)
            ? Task.FromResult((string?)value)
            : Task.FromResult((string?)null);
    }

    public Task SetAsync(string value, TimeSpan? expiration = null)
    {
        _data[_key] = value;
        return Task.CompletedTask;
    }

    public Task TryDeleteAsync()
    {
        _data.Remove(_key);

        return Task.CompletedTask;
    }

    public Task<string?> TryGetAsync() => throw new NotImplementedException();
}

internal class MockRedisCache<T>
    : IRedisCache<T> where T : class
{
    private readonly Dictionary<string, object> _data = [];
    private readonly string _key;

    public IReadOnlyDictionary<string, object> Data => _data;

    public MockRedisCache(string key, Dictionary<string, object> data)
    {
        _key = typeof(T).Name + "_" + key;
        _data = data;
    }

    public Task SetAsync(T value, TimeSpan? expiration = null)
    {
        _data[_key] = value;
        return Task.CompletedTask;
    }

    public Task<T?> TryDeleteAsync()
    {
        _data.Remove(_key, out object? value);

        if (value is null)
        {
            return Task.FromResult(default(T?));
        }
        else
        {
            return Task.FromResult((T?)value);
        }
    }

    public Task<T?> TryGetStateAsync()
    {
        return _data.TryGetValue(_key, out object? value)
            ? Task.FromResult<T?>((T?)value)
            : Task.FromResult(default(T?));
    }
}
