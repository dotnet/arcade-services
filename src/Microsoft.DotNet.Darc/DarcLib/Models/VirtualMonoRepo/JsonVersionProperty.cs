// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
public class JsonVersionProperty : VersionFileProperty
{
    public string Name { get; }
    public NodeComparisonResult Result { get; }
    public object? NewValue { get; }

    public JsonVersionProperty(string jsonVersionPropertyName, NodeComparisonResult result, object? newValue = null)
    {
        Name = jsonVersionPropertyName;
        Result = result;
        NewValue = newValue;
    }

    public override string GetName() => Name;
    public override bool IsAdded() => Result == NodeComparisonResult.Added;
    public override bool IsRemoved() => Result == NodeComparisonResult.Removed;
    public override bool IsUpdated() => Result == NodeComparisonResult.Updated;
    public override bool IsGreater(VersionFileProperty otherProperty)
    {
        // Are these even comparable?
        if (otherProperty.GetType() != typeof(JsonVersionProperty))
        {
            throw new ArgumentException($"Cannot compare {GetType()} with {otherProperty.GetType()}");
        }
        // Is one of them null?
        var property = (JsonVersionProperty)otherProperty;
        if (NewValue == null && property == null)
        {
            throw new ArgumentException("Cannot compare null properties.");
        }
        if (NewValue == null)
        {
            return false;
        }
        if (property.NewValue == null) 
        {
            return true;
        }

        // Are the value types the same?
        if (NewValue.GetType() != property.NewValue.GetType())
        {
            throw new ArgumentException($"Cannot compare {GetType()} with {otherProperty.GetType()} because their values are of different types.");
        }

        if (NewValue.GetType() == typeof(List<string>))
        {
            throw new ArgumentException($"Cannot compare properties with {nameof(List<string>)} values.");
        }

        if (NewValue.GetType() == typeof(bool))
        {
            // if values are different, throw an exception
            if (!NewValue.Equals(property.NewValue))
            {
                throw new ArgumentException($"Key {Name} value has different boolean values in properties.");
            }
            return true;
        }

        if (NewValue.GetType() == typeof(string))
        {
            // if we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(NewValue.ToString()!, out var thisVersion) &&
                SemanticVersion.TryParse(property.NewValue.ToString()!, out var otherVersion))
            {
                return thisVersion > otherVersion;
            }
            // if we can't parse both values as SemanticVersion, that means one is using a different property like $(Version), so throw an exception
            throw new ArgumentException($"Key {Name} value has different string values in properties, and cannot be parsed as SemanticVersion");
        }

        throw new ArgumentException($"Cannot compare properties with {NewValue.GetType()} values.");
    }
}
