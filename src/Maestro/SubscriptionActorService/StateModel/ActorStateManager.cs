// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Data;

#nullable enable
namespace SubscriptionActorService.StateModel;

/// <summary>
/// Wrapper class that binds the key under which the state is stored.
/// </summary>
internal class ActorStateManager<T> where T : class
{
    private const int DefaultDueTimeInMinutes = 5;

    private readonly IActorStateManager _stateManager;
    private readonly IReminderManager _reminderManager;
    private readonly string _key;

    public ActorStateManager(
        IActorStateManager stateManager,
        IReminderManager reminderManager,
        string key)
    {
        _stateManager = stateManager;
        _reminderManager = reminderManager;
        _key = key;
    }

    public async Task<T?> TryGetStateAsync()
    {
        ConditionalValue<T> value = await _stateManager.TryGetStateAsync<T>(_key);
        return value.HasValue ? value.Value : null;
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

    public async Task SetReminderAsync(int dueTimeInMinutes = DefaultDueTimeInMinutes)
    {
        await _reminderManager.TryRegisterReminderAsync(
            _key,
            [],
            TimeSpan.FromMinutes(dueTimeInMinutes),
            TimeSpan.FromMinutes(dueTimeInMinutes));
    }

    public async Task UnsetReminderAsync()
    {
        await _reminderManager.TryUnregisterReminderAsync(_key);
    }
}
