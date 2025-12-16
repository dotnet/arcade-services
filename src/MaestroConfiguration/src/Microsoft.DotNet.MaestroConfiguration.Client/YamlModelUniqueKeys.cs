// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Provides unique key extractors for YAML models.
/// </summary>
public static class YamlModelUniqueKeys
{
    private static readonly IReadOnlyDictionary<Type, object> _keyExtractors = new Dictionary<Type, object>()
    {
        { typeof(SubscriptionYaml), new SubscriptionYamlUniqueKey() },
        { typeof(DefaultChannelYaml), new DefaultChannelYamlUniqueKey() },
        { typeof(ChannelYaml), new ChannelYamlUniqueKey() },
        { typeof(BranchMergePoliciesYaml), new BranchMergePoliciesYamlUniqueKey() },
    };

    /// <summary>
    /// Gets the unique key for a YAML model instance.
    /// </summary>
    /// <typeparam name="T">The type of YAML model.</typeparam>
    /// <param name="model">The model to get the unique key for.</param>
    /// <returns>A unique key string that can be used for comparison and hashing.</returns>
    public static string GetUniqueKey<T>(T model) where T : IYamlModel
    {
        if (_keyExtractors.TryGetValue(typeof(T), out var extractor))
        {
            return ((IUniqueKeyExtractor<T>)extractor).GetUniqueKey(model);
        }

        throw new InvalidOperationException($"No unique key extractor registered for type {typeof(T).Name}");
    }

    /// <summary>
    /// Gets the unique key extractor for a YAML model type.
    /// </summary>
    /// <typeparam name="T">The type of YAML model.</typeparam>
    /// <returns>The unique key extractor for the type.</returns>
    public static IUniqueKeyExtractor<T> GetExtractor<T>() where T : IYamlModel
    {
        if (_keyExtractors.TryGetValue(typeof(T), out var extractor))
        {
            return (IUniqueKeyExtractor<T>)extractor;
        }

        throw new InvalidOperationException($"No unique key extractor registered for type {typeof(T).Name}");
    }
}

/// <summary>
/// Interface for extracting unique keys from YAML models.
/// </summary>
/// <typeparam name="T">The type of YAML model.</typeparam>
public interface IUniqueKeyExtractor<in T> where T : IYamlModel
{
    /// <summary>
    /// Gets the unique key for a model instance.
    /// </summary>
    /// <param name="model">The model to get the unique key for.</param>
    /// <returns>A unique key string that can be used for comparison and hashing.</returns>
    string GetUniqueKey(T model);
}

/// <summary>
/// Unique key extractor for <see cref="SubscriptionYaml"/> instances.
/// </summary>
public class SubscriptionYamlUniqueKey : IUniqueKeyExtractor<SubscriptionYaml>
{
    public string GetUniqueKey(SubscriptionYaml model)
        => model.Id.ToString();
}

/// <summary>
/// Unique key extractor for <see cref="DefaultChannelYaml"/> instances.
/// </summary>
public class DefaultChannelYamlUniqueKey : IUniqueKeyExtractor<DefaultChannelYaml>
{
    public string GetUniqueKey(DefaultChannelYaml model)
        => $"{model.Repository}|{model.Branch}|{model.Channel}";
}

/// <summary>
/// Unique key extractor for <see cref="ChannelYaml"/> instances.
/// </summary>
public class ChannelYamlUniqueKey : IUniqueKeyExtractor<ChannelYaml>
{
    public string GetUniqueKey(ChannelYaml model)
     => model.Name;
}

/// <summary>
/// Unique key extractor for <see cref="BranchMergePoliciesYaml"/> instances.
/// </summary>
public class BranchMergePoliciesYamlUniqueKey : IUniqueKeyExtractor<BranchMergePoliciesYaml>
{
    public string GetUniqueKey(BranchMergePoliciesYaml model)
        => $"{model.Repository}|{model.Branch}";
}
