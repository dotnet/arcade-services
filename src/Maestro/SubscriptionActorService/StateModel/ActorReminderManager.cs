// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;

#nullable enable
namespace SubscriptionActorService.StateModel;

/// <summary>
/// Wrapper class that binds the key under which the reminders are stored.
/// </summary>
internal class ActorReminderManager<T> where T : class
{
    private readonly IReminderManager _reminderManager;
    private readonly string _key;

    public ActorReminderManager(IReminderManager reminderManager, string key)
    {
        _reminderManager = reminderManager;
        _key = key;
    }

    public virtual async Task SetReminderAsync(int dueTimeInMinutes = ActorStateManager<T>.DefaultDueTimeInMinutes)
    {
        await _reminderManager.TryRegisterReminderAsync(
            _key,
            null,
            TimeSpan.FromMinutes(dueTimeInMinutes),
            TimeSpan.FromMinutes(dueTimeInMinutes));
    }

    public async Task UnsetReminderAsync()
    {
        await _reminderManager.TryUnregisterReminderAsync(_key);
    }
}
