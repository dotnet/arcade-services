// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;
using JsonChanges = (System.Collections.Generic.List<string> removals,
        System.Collections.Generic.List<(string, object)> additions,
        System.Collections.Generic.List<(string, object)> updates);

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;
public class FlatJsonChangeComparer
{
    public static JsonChanges ComputeChanges(IList<FlatJsonComparerResultNode> repoChanges, IList<FlatJsonComparerResultNode> vmrChanges)
    {
        List<string> changedProperties = repoChanges
            .Concat(vmrChanges)
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        List<string> removals = [];
        List<(string, object)> additions = [];
        List<(string, object)> updates = [];
        foreach (string property in changedProperties)
        {
            var repoChange = repoChanges.FirstOrDefault(c => c.Key == property);
            var vmrChange = vmrChanges.FirstOrDefault(c => c.Key == property);

            bool addedInRepo = repoChange != null && repoChange.Result == NodeComparisonResult.Added;
            bool addedInVmr = vmrChange != null && vmrChange.Result == NodeComparisonResult.Added;
            bool removedInRepo = repoChange != null && repoChange.Result == NodeComparisonResult.Removed;
            bool removedInVmr = vmrChange != null && vmrChange.Result == NodeComparisonResult.Removed;
            bool updatedInRepo = repoChange != null && repoChange.Result == NodeComparisonResult.Updated;
            bool updatedInVmr = vmrChange != null && vmrChange.Result == NodeComparisonResult.Updated;

            if (removedInRepo)
            {
                if (addedInVmr)
                {
                    throw new ArgumentException($"Key {property} is added in one repo and removed in the other json, which is not allowed.");
                }
                // we don't have to do anything since the property is removed in the repo
                continue;
            }

            if (removedInVmr)
            {
                if (addedInRepo)
                {
                    throw new ArgumentException($"Key {property} is added in one repo and removed in the other json, which is not allowed.");
                }
                removals.Add(property);
                continue;
            }

            if (addedInRepo && addedInVmr)
            {
                ProcessSimultaneousUpdates(repoChange!, vmrChange!, additions);
                continue;
            }
            if (addedInRepo)
            {
                // we don't have to do anything since the property is added in the repo
                continue;
            }
            if (addedInVmr)
            {
                additions.Add((property, vmrChange!.NewValue!));
                continue;
            }

            if (updatedInRepo && updatedInVmr)
            {        
                ProcessSimultaneousUpdates(repoChange!, vmrChange!, updates);
                continue;
            }
            if (updatedInRepo)
            {
                // we don't have to do anything since the property is updated in the repo
                continue;
            }
            if (updatedInVmr)
            {
                updates.Add((property, vmrChange!.NewValue!));
                continue;
            }
        }

        return (removals, additions, updates);
    }

    private static void ProcessSimultaneousUpdates(FlatJsonComparerResultNode repoChange, FlatJsonComparerResultNode vmrChange, List<(string, object)> updates)
    {
        if (repoChange.NewValue == null && vmrChange.NewValue == null)
        {
            // Both are null, no action needed
            return;
        }
        if (repoChange.NewValue == null)
        {
            // Only repo change is null, treat it as an update with vmr value
            updates.Add((repoChange.Key, vmrChange.NewValue!));
            return;
        }

        if (repoChange.NewValue?.GetType() != vmrChange.NewValue?.GetType())
        {
            throw new ArgumentException($"Key {repoChange.Key} value has different types in repo and vmr json");
        }

        if (repoChange.NewValue!.GetType() == typeof(List<string>))
        {
            // not sure about this, maybe we just don't allow this?
            var updatedList = ((List<string>)repoChange.NewValue!)
                .Concat((List<string>)vmrChange.NewValue!)
                .Distinct()
                .ToList();
            updates.Add((repoChange.Key, updatedList));
            return;
        }
        if (repoChange.NewValue!.GetType() == typeof(bool))
        {
            // if values are different, throw an exception
            if (!repoChange.NewValue.Equals(vmrChange.NewValue))
            {
                throw new ArgumentException($"Key {repoChange.Key} value has different boolean values in repo and vmr json");
            }
            updates.Add((repoChange.Key, repoChange.NewValue!));
            return;
        }
        if (repoChange.NewValue!.GetType() == typeof(string))
        {
            // if we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(repoChange.NewValue!.ToString()!, out var repoVersion) &&
                SemanticVersion.TryParse(vmrChange.NewValue!.ToString()!, out var vmrVersion))
            {
                updates.Add((repoChange.Key, repoVersion > vmrVersion ? repoChange.NewValue! : vmrChange.NewValue!));
                return;
            }

            // if we can't parse both values as SemanticVersion, that means one is using a different property like $(Version), so throw an exception
            throw new ArgumentException($"Key {repoChange.Key} value has different string values in repo and vmr json, and cannot be parsed as SemanticVersion");
        }
    }

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
