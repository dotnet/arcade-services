// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;
public static class JsonFlattener
{
    public static Dictionary<string, object> FlattenJsonToDictionary(string json)
    {
        using var document = JsonDocument.Parse(json);
        Dictionary<string, object> dictionary = [];
        Queue<(JsonElement element, string path)> pathsToProcess = [];
        pathsToProcess.Enqueue((document.RootElement, string.Empty));

        while (pathsToProcess.Count > 0)
        {
            (JsonElement currentElement, string currentPath) = pathsToProcess.Dequeue();

            FlattenJsonElement(currentElement, currentPath, dictionary, pathsToProcess);
        }

        return dictionary;
    }

    /// <summary>
    /// This method is used to flatten global.json and dotnet-tools.json files into a dictionary, which don't have a complex structure.
    /// For example, we can expect that arrays won't have other arrays inside of them
    /// </summary>
    private static void FlattenJsonElement(JsonElement element, string path, Dictionary<string, object> dictionary, Queue<(JsonElement, string)> pathsToProcess)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach(JsonProperty property in element.EnumerateObject())
                {
                    string newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}:{property.Name}";
                    pathsToProcess.Enqueue((property.Value, newPath));
                }
                break;

            case JsonValueKind.Array:
                dictionary[path] = element.EnumerateArray().Select(item => item.ToString()).ToList();
                break;

            case JsonValueKind.True:
                dictionary[path] = true;
                break;

            case JsonValueKind.False:
                dictionary[path] = false;
                break;

            default:
                dictionary[path] = element.ToString();
                break;
        }
    }
}
