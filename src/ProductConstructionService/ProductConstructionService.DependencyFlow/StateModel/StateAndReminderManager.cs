// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;

namespace ProductConstructionService.DependencyFlow.StateModel;

/// <summary>
/// Wrapper class that binds the key under which the state and reminders are stored.
/// </summary>
internal class StateAndReminderManager<T>
    : StateManager<T> where T : class
{
    private readonly IReminderManager _reminderManager;
    private readonly string _key;

    public StateAndReminderManager(
            IStateManager stateManager,
            IReminderManager reminderManager,
            ILogger logger,
            string key)
        : base(stateManager, logger, key)
    {
        _reminderManager = reminderManager;
        _key = key;
    }

    public virtual async Task SetReminderAsync(int dueTimeInMinutes = ReminderManager<T>.DefaultDueTimeInMinutes)
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
