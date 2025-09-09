// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    public bool IsAdded => _result == NodeComparisonResult.Added;
    public bool IsRemoved => _result == NodeComparisonResult.Removed;
    public bool IsUpdated => _result == NodeComparisonResult.Updated;
}

public enum NodeComparisonResult
{
    Added,
    Removed,
    Updated
}
