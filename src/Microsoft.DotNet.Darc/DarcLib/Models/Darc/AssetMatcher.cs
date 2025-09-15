// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

public static class AssetFilterExtensions
{
    /// <summary>
    /// Creates a matcher for a given set of filters.
    /// </summary>
    /// <param name="filters">Collection of asset filter patterns</param>
    /// <returns>Matcher instance or null if no patterns</returns>
    public static IAssetMatcher GetAssetMatcher(this IReadOnlyCollection<string> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return new AssetMatcher(null);
        }

        var matcher = new Matcher();
        matcher.AddIncludePatterns(filters);
        return new AssetMatcher(matcher);
    }
}

public interface IAssetMatcher
{
    bool IsExcluded(string name);
}

public class AssetMatcher : IAssetMatcher
{
    private readonly Matcher _matcher;

    public AssetMatcher(Matcher matcher)
    {
        _matcher = matcher;
    }

    public bool IsExcluded(string name)
    {
        if (_matcher == null)
        {
            return false;
        }

        return _matcher.Match(name).HasMatches;
    }

    public bool IsExcluded(string name, UnixPath relativePath)
    {
        if (_matcher == null)
        {
            return false;
        }

        bool isRoot = relativePath == UnixPath.Empty;
        return IsExcluded(isRoot ? name : $"{relativePath}/{name}");
    }
}
