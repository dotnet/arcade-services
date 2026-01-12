// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests.Mocks;

internal class MockReminderManagerFactory : IReminderManagerFactory
{
    public Dictionary<string, object> Reminders { get; } = [];

    public IReminderManager<T> CreateReminderManager<T>(string key, bool isCodeFlow) where T : WorkItem
    {
        key = $"{typeof(T).Name}_{key}";
        return new MockReminderManager<T>(key, Reminders);
    }
}
