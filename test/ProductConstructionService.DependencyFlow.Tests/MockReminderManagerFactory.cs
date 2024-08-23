// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests;

internal class MockReminderManagerFactory : IReminderManagerFactory
{
    private readonly Dictionary<string, MockReminderManager> _reminders = [];

    public IReadOnlyDictionary<string, MockReminderManager> Reminders => _reminders;

    public IReminderManager<T> CreateReminderManager<T>(string key) where T : WorkItem
    {
        MockReminderManager<T> manager;
        if (!_reminders.TryGetValue(key, out MockReminderManager? reminderManager))
        {
            manager = new MockReminderManager<T>(key);
            _reminders[key] = manager;
        }
        else
        {
            manager = (MockReminderManager<T>)reminderManager;
        }

        return manager;
    }
}
