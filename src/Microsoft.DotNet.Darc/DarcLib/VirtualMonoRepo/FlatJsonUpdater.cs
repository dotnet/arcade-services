// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IFlatJsonUpdater
{
    /// <summary>
    /// Applies a set of changes calculated by the VmrVersionFileMerger to a JSON file.
    /// </summary>
    /// <param name="fileContents">Contents of the file</param>
    /// <param name="changes">Changes we're applying</param>
    string ApplyJsonChanges(string fileContents, VersionFileChanges<JsonVersionProperty> changes);
}

/// <summary>
/// Applies a set of changes calculated by the VmrVersionFileMerger to a JSON file.
/// </summary>
public class FlatJsonUpdater : IFlatJsonUpdater
{
    private readonly ILogger<FlatJsonUpdater> _logger;

    public FlatJsonUpdater(ILogger<FlatJsonUpdater> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Applies a set of changes calculated by the VmrVersionFileMerger to a JSON file.
    /// </summary>
    /// <param name="fileContents">Contents of the file</param>
    /// <param name="changes">Changes we're applying</param>
    public string ApplyJsonChanges(
        string fileContents,
        VersionFileChanges<JsonVersionProperty> changes)
    {
        JsonNode rootNode = JsonNode.Parse(fileContents) ?? throw new InvalidOperationException("Failed to parse JSON file.");

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
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private void RemoveJsonProperty(JsonNode root, string path)
    {
        var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);

        // Keep track of all nodes we traverse so we can clean up empty parents
        List<JsonObject> parentChain = [];

        JsonNode currentNode = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (currentNode is not JsonObject obj || !obj.ContainsKey(segments[i]))
            {
                // Property does not exist; nothing to remove
                _logger.LogInformation("Property {PropertyPath} does not exist in JSON structure; skipping removal.", path);
                return;
            }

            parentChain.Add(obj);
            currentNode = obj[segments[i]]!;
        }

        // Remove the property from its parent
        if (currentNode is JsonObject parentObject)
        {
            string propertyToRemove = segments.Last();
            parentObject.Remove(propertyToRemove);
            parentChain.Add(parentObject);
        }

        // Clean up empty parent objects
        for (int i = parentChain.Count - 1; i > 0; i--)
        {
            if (parentChain[i].Count == 0)
            {
                parentChain[i - 1].Remove(segments[i - 1]);
            }
            else
            {
                break;
            }
        }
    }

    private void AddOrUpdateJsonProperty(JsonNode root, JsonVersionProperty property)
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
}
