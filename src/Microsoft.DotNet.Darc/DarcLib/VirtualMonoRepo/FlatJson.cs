// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class FlatJson
{
    public Dictionary<string, object> FlatValues { get; }

    public FlatJson(Dictionary<string, object> values)
    {
        FlatValues = values;
    }

    public static FlatJson Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        Dictionary<string, object> dictionary = [];
        Queue<(JsonElement element, string path)> pathsToProcess = [];
        pathsToProcess.Enqueue((document.RootElement, string.Empty));

        while (pathsToProcess.Count > 0)
        {
            (JsonElement currentElement, string currentPath) = pathsToProcess.Dequeue();

            FlattenSimpleJsonConfigElement(currentElement, currentPath, dictionary, pathsToProcess);
        }

        return new FlatJson(dictionary);
    }

    public List<JsonVersionProperty> GetDiff(FlatJson otherJson)
    {
        List<JsonVersionProperty> changes = [];

        foreach (var kvp in FlatValues)
        {
            if (!otherJson.FlatValues.TryGetValue(kvp.Key, out var newValue))
            {
                changes.Add(new JsonVersionProperty(kvp.Key, NodeComparisonResult.Removed, null));
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
                    changes.Add(new JsonVersionProperty(kvp.Key, NodeComparisonResult.Updated, newValue));
                }
            }
            else if (!kvp.Value.Equals(newValue))
            {
                changes.Add(new JsonVersionProperty(kvp.Key, NodeComparisonResult.Updated, newValue));
            }
        }

        foreach (var kvp in otherJson.FlatValues)
        {
            if (!FlatValues.ContainsKey(kvp.Key))
            {
                changes.Add(new JsonVersionProperty(kvp.Key, NodeComparisonResult.Added, kvp.Value));
            }
        }

        return changes;
    }

    private static void FlattenSimpleJsonConfigElement(JsonElement element, string path, Dictionary<string, object> dictionary, Queue<(JsonElement, string)> pathsToProcess)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
                    pathsToProcess.Enqueue((property.Value, newPath));
                }
                break;

            case JsonValueKind.Array:
                ValidateJsonStringArray(element);
                dictionary[path] = element.EnumerateArray().Select(item => item.ToString()).ToList();
                break;

            case JsonValueKind.True:
                dictionary[path] = true;
                break;

            case JsonValueKind.False:
                dictionary[path] = false;
                break;

            case JsonValueKind.Number:
                dictionary[path] = element.GetInt32(); // Assuming that all numbers are integers
                break;

            default:
                dictionary[path] = element.ToString();
                break;
        }
    }

    private static void ValidateJsonStringArray(JsonElement arrayElement)
    {
        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected an array element.");
        }
        foreach (JsonElement item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Expected all items in the array to be strings.");
            }
        }
    }
}
