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
    private string _name { get; }
    private NodeComparisonResult _result { get; }
    private object? _newValue { get; }

    public JsonVersionProperty(string jsonVersionPropertyName, NodeComparisonResult result, object? newValue = null)
    {
        _name = jsonVersionPropertyName;
        _result = result;
        _newValue = newValue;
    }

    public override string Name => _name;

    public override object? Value => _newValue;

    public override bool IsAdded() => _result == NodeComparisonResult.Added;
    public override bool IsRemoved() => _result == NodeComparisonResult.Removed;
    public override bool IsUpdated() => _result == NodeComparisonResult.Updated;
    public override bool IsGreater(VersionFileProperty otherProperty)
    {
        // Are these even comparable?
        if (otherProperty.GetType() != typeof(JsonVersionProperty))
        {
            throw new ArgumentException($"Cannot compare {GetType()} with {otherProperty.GetType()}");
        }
        // Is one of them null?
        var property = (JsonVersionProperty)otherProperty;
        if (_newValue == null && property == null)
        {
            throw new ArgumentException("Cannot compare null properties.");
        }
        if (_newValue == null)
        {
            return false;
        }
        if (property._newValue == null) 
        {
            return true;
        }

        // Are the value types the same?
        if (_newValue.GetType() != property._newValue.GetType())
        {
            throw new ArgumentException($"Cannot compare {GetType()} with {otherProperty.GetType()} because their values are of different types.");
        }

        if (_newValue.GetType() == typeof(List<string>))
        {
            throw new ArgumentException($"Cannot compare properties with {nameof(List<string>)} values.");
        }

        if (_newValue.GetType() == typeof(bool))
        {
            // if values are different, throw an exception
            if (!_newValue.Equals(property._newValue))
            {
                throw new ArgumentException($"Key {Name} value has different boolean values in properties.");
            }
            return true;
        }

        if (_newValue.GetType() == typeof(int))
        {
            return (int)_newValue > (int)property._newValue;
        }

        if (_newValue.GetType() == typeof(string))
        {
            // if we're able to parse both values as SemanticVersion, take the bigger one
            if (SemanticVersion.TryParse(_newValue.ToString()!, out var thisVersion) &&
                SemanticVersion.TryParse(property._newValue.ToString()!, out var otherVersion))
            {
                return thisVersion > otherVersion;
            }
            // if we can't parse both values as SemanticVersion, that means one is using a different property like $(Version), so throw an exception
            throw new ArgumentException($"Key {Name} value has different string values in properties, and cannot be parsed as SemanticVersion");
        }

        throw new ArgumentException($"Cannot compare properties with {_newValue.GetType()} values.");
    }
}
