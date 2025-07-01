// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonChanges = (System.Collections.Generic.List<string> removals,
        System.Collections.Generic.List<(string, object)> additions,
        System.Collections.Generic.List<(string, object)> updates);

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;
public class FlatJsonChangeComparer
{
    public static string ApplyChanges(
        string file,
        JsonChanges changes)
    {
        JsonNode rootNode = JsonNode.Parse(file) ?? throw new InvalidOperationException("Failed to parse JSON file.");

        foreach (string removal in changes.removals)
        {
            RemoveJsonProperty(rootNode, removal);
        }
        foreach ((string path, object value) in changes.additions.Concat(changes.updates))
        {
            AddOrUpdateJsonProperty(rootNode, path, value);
        }

        return rootNode.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
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

    private static void AddOrUpdateJsonProperty(JsonNode root, string path, object value)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
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
            parentObject[propertyToAdd] = JsonValue.Create(value);
        }
    }
}
