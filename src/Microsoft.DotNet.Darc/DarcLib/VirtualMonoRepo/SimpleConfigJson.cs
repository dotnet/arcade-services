// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;
public class SimpleConfigJson
{
    public Dictionary<string, object> Dictionary { get; }

    public SimpleConfigJson(Dictionary<string, object> dictionary)
    {
        Dictionary = dictionary;
    }

    public static SimpleConfigJson Parse(string json)
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

        return new SimpleConfigJson(dictionary);
    }

    public List<JsonVersionProperty> GetDiff(SimpleConfigJson otherJson)
    {
        List<JsonVersionProperty> changes = [];

        foreach (var kvp in this.Dictionary)
        {
            if (!otherJson.Dictionary.TryGetValue(kvp.Key, out var newValue))
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

        foreach (var kvp in otherJson.Dictionary)
        {
            if (!this.Dictionary.ContainsKey(kvp.Key))
            {
                changes.Add(new JsonVersionProperty(kvp.Key, NodeComparisonResult.Added, kvp.Value));
            }
        }

        return changes;
    }

    /// <summary>
    /// Applies a set of changes calculated by the VmrVersionFileMerger to a JSON file.
    /// </summary>
    /// <param name="file">Contents of the file</param>
    /// <param name="changes">Changes we're applying</param>
    public static string ApplyJsonChanges(
        string file,
        VersionFileChanges<JsonVersionProperty> changes)
    {
        JsonNode rootNode = JsonNode.Parse(file) ?? throw new InvalidOperationException("Failed to parse JSON file.");

        foreach (string removal in changes.Removals)
        {
            RemoveJsonProperty(rootNode, removal);
        }
        foreach (var change in changes.Additions.Values.Concat(changes.Updates.Values))
        {
            AddOrUpdateJsonProperty(rootNode, change);
        }

        return rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        });
    }

    private static void RemoveJsonProperty(JsonNode root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);

        JsonNode currentNode = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (currentNode is not JsonObject obj || !obj.ContainsKey(segments[i]))
            {
                throw new InvalidOperationException($"Cannot navigate to {segments[i]} in JSON structure.");
            }

            currentNode = obj[segments[i]]!;
        }

        // Remove the property from its parent
        if (currentNode is JsonObject parentObject)
        {
            string propertyToRemove = segments[segments.Length - 1];
            parentObject.Remove(propertyToRemove);
        }
    }

    private static void AddOrUpdateJsonProperty(JsonNode root, JsonVersionProperty property)
    {
        var segments = property.Name.Split(':', StringSplitOptions.RemoveEmptyEntries);
        JsonNode currentNode = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (currentNode is not JsonObject obj)
            {
                throw new InvalidOperationException($"Cannot navigate to {segments[i]} in JSON structure.");
            }
            if (!obj.ContainsKey(segments[i]))
            {
                obj[segments[i]] = new JsonObject();
            }
            currentNode = obj[segments[i]]!;
        }
        // Add the property to its parent
        if (currentNode is JsonObject parentObject)
        {
            string propertyToAdd = segments[segments.Length - 1];
            parentObject[propertyToAdd] = JsonValue.Create(property.Value);
        }
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
