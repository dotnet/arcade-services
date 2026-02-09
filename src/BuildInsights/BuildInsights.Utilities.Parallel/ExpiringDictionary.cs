// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

namespace BuildInsights.Utilities.Parallel;

public class ExpiringDictionary<TKey, TValue>
{
    private class ExpiringCollectionValue
    {
        public readonly TValue Value;
        public readonly DateTimeOffset Added;

        public ExpiringCollectionValue(TValue value)
        {
            Value = value;
            Added = DateTimeOffset.UtcNow;
        }
    }

    private readonly ConcurrentDictionary<TKey, ExpiringCollectionValue> _items;
    private readonly TimeSpan _ageToKeep;
    private DateTimeOffset _lastCheck;
    private readonly object _mutex = 42;

    public ExpiringDictionary(TimeSpan minimumAgeToKeep)
    {
        _lastCheck = DateTimeOffset.UtcNow;
        _items = new ConcurrentDictionary<TKey, ExpiringCollectionValue>();
        _ageToKeep = minimumAgeToKeep;
    }

    public bool TryAdd(TKey key, Func<TValue> generator, out TValue value)
    {
        RemoveExpiredValues();
        ExpiringCollectionValue newValue = null;
        ExpiringCollectionValue dictionaryValue = _items.GetOrAdd(key, k => newValue = new ExpiringCollectionValue(generator()));
        value = dictionaryValue.Value;
        return newValue != null && ReferenceEquals(newValue, dictionaryValue);
    }

    public TValue this[TKey index] => _items.TryGetValue(index, out var value) ? value.Value : default;

    private void RemoveExpiredValues()
    {
        DateTimeOffset tooOld = DateTimeOffset.UtcNow.Subtract(_ageToKeep);
        // Just in case this gets hammered, only do this check once every 'ageToKeep'
        if (_lastCheck >= tooOld)
        {
            return;
        }

        lock (_mutex)
        {
            if (_lastCheck >= tooOld)
            {
                return;
            }

            _lastCheck = DateTimeOffset.UtcNow;
            foreach (var (key, value) in _items)
            {
                if (value.Added >= tooOld)
                {
                    continue;
                }

                // Ignore failure, we'll try again later
                // Concurrency errors are unlikely to persist
                _items.TryRemove(key, out _);
            }
        }
    }
}
