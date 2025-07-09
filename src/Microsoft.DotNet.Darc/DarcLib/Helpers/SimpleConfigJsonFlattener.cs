// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// This class is used to flatten simple json config files like global.json and dotnet-tools.json files into a dictionary.
/// It only supports a limited set of types and structures, which are expected to be simple.
/// For example, we can expect that arrays will only have strings inside of them
/// </summary>
public static class SimpleConfigJsonFlattener
{
    public static Dictionary<string, object> FlattenSimpleConfigJsonToDictionary(string json)
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

        return dictionary;
    }

    private static void FlattenSimpleJsonConfigElement(JsonElement element, string path, Dictionary<string, object> dictionary, Queue<(JsonElement, string)> pathsToProcess)
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

    public static void ValidateJsonStringArray(JsonElement arrayElement)
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
