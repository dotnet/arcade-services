// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.WorkItems;

namespace ProductConstructionService.DependencyFlow.Tests.Mocks;

internal abstract class MockReminderManager
{
}

internal class MockReminderManager<T>
    : MockReminderManager, IReminderManager<T> where T : WorkItem
{
    public readonly Dictionary<string, object> Data;
    private readonly string _key;

    public MockReminderManager(string key, Dictionary<string, object> data)
    {
        _key = key;
        Data = data;
    }

    public Task SetReminderAsync(T reminder, TimeSpan dueTime)
    {
        Data[_key] = reminder;
        return Task.CompletedTask;
    }

    public Task UnsetReminderAsync()
    {
        Data.Remove(_key);
        return Task.CompletedTask;
    }

    public Task ReminderReceivedAsync()
    {
        Data.Remove(_key);
        return Task.CompletedTask;
    }
}
