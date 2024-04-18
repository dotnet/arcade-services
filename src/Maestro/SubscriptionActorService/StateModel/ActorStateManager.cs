// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Data;

#nullable enable
namespace SubscriptionActorService.StateModel;

/// <summary>
/// Wrapper class that binds the key under which the state is stored.
/// </summary>
internal class ActorStateManager<T> where T : class
{
    public const int DefaultDueTimeInMinutes = 5;

    private readonly IActorStateManager _stateManager;
    private readonly ILogger _logger;
    private readonly string _key;

    public ActorStateManager(
        IActorStateManager stateManager,
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
            ConditionalValue<T> value = await _stateManager.TryGetStateAsync<T>(_key);
            return value.HasValue ? value.Value : null;
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
