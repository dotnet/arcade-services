// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow.StateModel;

internal interface IStateManager
{
    Task AddOrUpdateStateAsync<T>(string key, List<T> ts, Func<object, List<T>, object> value);
    Task SaveStateAsync();
    Task SetStateAsync<T>(string key, T value) where T : class;
    Task<T?> TryGetStateAsync<T>(string key) where T : class;
    Task TryRemoveStateAsync(string key);
}

/// <summary>
/// Wrapper class that binds the key under which the state is stored.
/// </summary>
internal class StateManager<T> where T : class
{
    private readonly IStateManager _stateManager;
    private readonly ILogger _logger;
    private readonly string _key;

    public StateManager(
        IStateManager stateManager,
        ILogger logger,
        string key)
    {
        _stateManager = stateManager;
        _logger = logger;
        _key = key;
    }

    public async Task<T?> TryGetStateAsync()
    {
        try
        {
            return await _stateManager.TryGetStateAsync<T>(_key);
        }
        catch (SerializationException e)
        {
            // If we can't deserialize (maybe the model changed?), we drop the state
            _logger.LogError(e, "Failed to deserialize state {type} stored in {key}. Removing from state memory", typeof(T).Name, _key);
            await RemoveStateAsync();
            return null;
        }
    }

    public async Task StoreStateAsync(T value)
    {
        await _stateManager.SetStateAsync(_key, value);
        await _stateManager.SaveStateAsync();
    }

    public async Task RemoveStateAsync()
    {
        await _stateManager.TryRemoveStateAsync(_key);
    }
}
