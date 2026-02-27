// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common.Cache;

namespace ProductConstructionService.WorkItem.Tests;
internal class FakeRedisCache : IRedisCache
{
    private string? _value;

    public Task SetAsync(string value, TimeSpan? expiration = null)
    {
        _value = value;
        return Task.CompletedTask;
    }

    public Task<bool> TryDeleteAsync() => throw new NotImplementedException();
    public Task<string?> TryGetAsync() => Task.FromResult(_value);
    public Task<string?> GetAsync() => Task.FromResult(_value);
    public Task<string?> GetAsync(string key) => throw new NotImplementedException();
    public IAsyncEnumerable<string> GetKeysAsync(string pattern) => throw new NotImplementedException();
}
