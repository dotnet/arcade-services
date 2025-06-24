// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public static class FlatJsonComparer
{
    public static void CompareFlatJsons(
        Dictionary<string, object> oldJson,
        Dictionary<string, object> newJson)
    {
        List<FlatJsonComparerResultNode> changes = [];

        foreach (var kvp in oldJson)
        {
            if (!newJson.TryGetValue(kvp.Key, out var newValue))
            {
                changes.Add(new FlatJsonComparerResultNode(kvp.Key, NodeComparisonResult.Removed, null));
            }
            else if (kvp.Value.GetType() != newValue.GetType())
            {
                throw new ArgumentException($"Key {kvp.Key} value has different types in old and new json");
            }
            else if (kvp.Value.GetType() == typeof(List<string>))
            {
                var oldList = (List<string>)kvp.Value;
                var newList = (List<string>)newValue;
                if (!oldList.SequenceEqual(newList))
                {
                    changes.Add(new FlatJsonComparerResultNode(kvp.Key, NodeComparisonResult.Updated, newValue));
                }
            }
            else if (!kvp.Value.Equals(newValue))
            {
                changes.Add(new FlatJsonComparerResultNode(kvp.Key, NodeComparisonResult.Updated, newValue));
            }
        }

        foreach (var kvp in newJson)
        {
            if (!oldJson.ContainsKey(kvp.Key))
            {
                changes.Add(new FlatJsonComparerResultNode(kvp.Key, NodeComparisonResult.Added, kvp.Value));
            }
        }
    }
}

public record FlatJsonComparerResultNode(string Key, NodeComparisonResult Result, object? NewValue);

public enum NodeComparisonResult
{
    Added,
    Removed,
    Updated
}
