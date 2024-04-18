// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Actors.Runtime;

#nullable enable
namespace SubscriptionActorService.StateModel;

/// <summary>
/// Wrapper class that binds the key under which the state is stored.
/// Allows to store multiple items of the same type in the state
/// </summary>
internal class ActorCollectionStateManager<T> : ActorStateAndReminderManager<List<T>>
{
    private readonly IActorStateManager _stateManager;
    private readonly string _key;

    public ActorCollectionStateManager(IActorStateManager stateManager, IReminderManager reminderManager, ILogger logger, string key)
        : base(stateManager, reminderManager, logger, key)
    {
        _stateManager = stateManager;
        _key = key;
    }

    public async Task StoreItemStateAsync(T value)
    {
        await _stateManager.AddOrUpdateStateAsync(
            _key,
            new List<T> { value },
            (_, old) =>
            {
                old.Add(value);
                return old;
            });
    }
}
