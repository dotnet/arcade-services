// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// A dictionary that treats null UnixPath keys as equivalent to UnixPath(".") for root directory operations.
/// This allows using null to represent the root directory while maintaining dictionary functionality.
/// </summary>
/// <typeparam name="TValue">The type of values in the dictionary</typeparam>
public class NullSafeUnixPathDictionary<TValue> : IEnumerable<KeyValuePair<UnixPath?, TValue>>
{
    private readonly Dictionary<UnixPath, TValue> _inner = new();
    private static readonly UnixPath RootPath = new(".");

    private static UnixPath NormalizeKey(UnixPath? key) 
    {
        if (key == null) return RootPath;
        
        var path = key.ToString();
        return path == "/" || path == "" || path == "." ? RootPath : key;
    }

    public TValue this[UnixPath? key]
    {
        get => _inner.TryGetValue(NormalizeKey(key), out var value) ? value : default!;
        set => _inner[NormalizeKey(key)] = value;
    }

    public bool ContainsKey(UnixPath? key) => _inner.ContainsKey(NormalizeKey(key));
    public bool TryGetValue(UnixPath? key, out TValue value) => _inner.TryGetValue(NormalizeKey(key), out value!);
    public bool Remove(UnixPath? key) => _inner.Remove(NormalizeKey(key));
    public void Clear() => _inner.Clear();
    public int Count => _inner.Count;
    public ICollection<UnixPath> Keys => _inner.Keys;
    public ICollection<TValue> Values => _inner.Values;

    public IEnumerator<KeyValuePair<UnixPath?, TValue>> GetEnumerator()
    {
        foreach (var kvp in _inner)
        {
            var originalKey = kvp.Key.Equals(RootPath) ? null : kvp.Key;
            yield return new KeyValuePair<UnixPath?, TValue>(originalKey, kvp.Value);
        }
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
