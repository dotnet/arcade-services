// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
public class JsonVersionProperty : IVersionFileProperty
{
    private string _name { get; }
    private NodeComparisonResult _result { get; }
    private object? _newValue { get; }

    public JsonVersionProperty(string jsonVersionPropertyName, NodeComparisonResult result, object? newValue = null)
    {
        _name = jsonVersionPropertyName;
        _result = result;
        _newValue = newValue;
    }

    public string Name => _name;

    public object? Value => _newValue;

    public bool IsAdded() => _result == NodeComparisonResult.Added;
    public bool IsRemoved() => _result == NodeComparisonResult.Removed;
    public bool IsUpdated() => _result == NodeComparisonResult.Updated;

    public static JsonVersionProperty SelectJsonVersionProperty(JsonVersionProperty repoProp, JsonVersionProperty vmrProp)
    {
        if (repoProp.Value == null && vmrProp.Value == null)
        {
            throw new ArgumentException($"Compared values for '{repoProp.Name}' are null");
        }
        if (repoProp.Value == null)
        {
            return vmrProp;
        }
        if (vmrProp.Value == null)
        {
            return repoProp;
        }

        // Are the value types the same?
        if (repoProp.Value.GetType() != vmrProp.Value.GetType())
        {
            throw new ArgumentException($"Cannot compare {repoProp.Value.GetType()} with {vmrProp.Value.GetType()} because their values are of different types.");
        }

        if (repoProp.Value.GetType() == typeof(List<string>))
        {
            throw new ArgumentException($"Cannot compare properties with {nameof(List<string>)} values.");
        }

        if (repoProp.Value.GetType() == typeof(bool))
        {
            // if values are different, throw an exception
            if (!repoProp.Value.Equals(vmrProp.Value))
            {
                throw new ArgumentException($"Key {repoProp.Name} value has different boolean values in properties.");
            }
            return repoProp;
        }

        if (repoProp.Value.GetType() == typeof(int))
        {
            return (int)repoProp.Value > (int)vmrProp.Value ? repoProp : vmrProp;
        }

        if (repoProp.Value.GetType() == typeof(string))
        {
            // if we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(repoProp.Value.ToString()!, out var repoVersion) &&
                SemanticVersion.TryParse(vmrProp.Value.ToString()!, out var vmrVersion))
            {
                return repoVersion > vmrVersion ? repoProp : vmrProp;
            }
            // if we can't parse both values as SemanticVersion, that means one is using a different property like $(Version), so throw an exception
            throw new ArgumentException($"Key {repoProp.Name} value has different string values in properties, and cannot be parsed as SemanticVersion");
        }

        throw new ArgumentException($"Cannot compare properties with {repoProp.Value.GetType()} values.");
    }
}

public enum NodeComparisonResult
{
    Added,
    Removed,
    Updated
}
