// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface IStateManager
{
    public void Set(string value);
    public Task SetAsync(string value);
    public bool TryGet(out string? value);
    public Task<string?> GetAsync();
}

public class StateManager : IStateManager
{
    private readonly string _stateKey;
    private readonly IConnectionMultiplexer _connection;

    private IDatabase Cache => _connection.GetDatabase();

    internal StateManager(IConnectionMultiplexer connection, string stateKey)
    {
        _connection = connection;
        _stateKey = stateKey;
    }

    public void Set(string value)
    {
        Cache.StringSet(_stateKey, value);
    }

    public async Task SetAsync(string value)
    {
        await Cache.StringSetAsync(_stateKey, value);
    }

    public bool TryGet(out string? value)
    {
        value = Cache.StringGet(_stateKey);

        return value != null;
    }

    public async Task<string?> GetAsync()
    {
        return await Cache.StringGetAsync(_stateKey);
    }

}
