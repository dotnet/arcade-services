// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItem.Tests;
internal class FakeRedisCache : IRedisCache
{
    private string? value;

    public Task<string?> GetAsync() => Task.FromResult(value);
    public Task SetAsync(string value, TimeSpan? expiration = null)
    {
        this.value = value;
        return Task.CompletedTask;
    }

    public Task TryDeleteAsync() => throw new NotImplementedException();
    public Task<string?> TryGetAsync() => Task.FromResult(value);
}
