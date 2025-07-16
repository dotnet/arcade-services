// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;


#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVersionPropertySelectorRegistry
{
    IVersionFileProperty SelectProperty(IVersionFileProperty repoProperty, IVersionFileProperty vmrProperty);

    /// <summary>
    /// Registers a selector for a specific type of version file property.
    /// </summary>
    void RegisterSelector<T>(IVersionPropertySelector<T> merger) where T : IVersionFileProperty;
}

internal interface ITypedSelector
{
    IVersionFileProperty Select(IVersionFileProperty repoProperty, IVersionFileProperty vmrProperty);
}

internal class TypedSelectorWrapper<T> : ITypedSelector where T : IVersionFileProperty
{
    private readonly IVersionPropertySelector<T> _selector;

    public TypedSelectorWrapper(IVersionPropertySelector<T> selector)
    {
        _selector = selector;
    }

    public IVersionFileProperty Select(IVersionFileProperty repoProperty, IVersionFileProperty vmrProperty)
    {
        return _selector.Select((T)repoProperty, (T)vmrProperty);
    }
}

public class VersionPropertySelectorRegistry : IVersionPropertySelectorRegistry
{
    private readonly Dictionary<Type, ITypedSelector> _selectors = new();

    public void RegisterSelector<T>(IVersionPropertySelector<T> selector) where T : IVersionFileProperty
    {
        _selectors[typeof(T)] = new TypedSelectorWrapper<T>(selector);
    }

    public IVersionFileProperty SelectProperty(IVersionFileProperty repoProperty, IVersionFileProperty vmrProperty)
    {
        if (repoProperty.GetType() != vmrProperty.GetType())
        {
            throw new ArgumentException($"Cannot merge properties of different types: {repoProperty.GetType().Name} and {vmrProperty.GetType().Name}");
        }

        var propertyType = repoProperty.GetType();
        
        if (!_selectors.TryGetValue(propertyType, out var merger))
        {
            throw new InvalidOperationException($"No merger registered for type {propertyType.Name}");
        }

        return merger.Select(repoProperty, vmrProperty);
    }
}
