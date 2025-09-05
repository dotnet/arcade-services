// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

    public static Dictionary<UnixPath, IAssetMatcher> GetAssetMatchers(this IReadOnlyCollection<string> filters, List<UnixPath> targetDirectories)
    {
        Dictionary<UnixPath, IAssetMatcher> assetMatcherDictionary = [];
        foreach (var dir in targetDirectories)
        {
            var directoryFilters = GetFiltersForDirectory(filters, dir);

            if (directoryFilters == null || directoryFilters.Count == 0)
            {
                assetMatcherDictionary[dir] = new AssetMatcher(null);
            }
            else
            {
                var matcher = new Matcher();
                matcher.AddIncludePatterns(directoryFilters);
                assetMatcherDictionary[dir] = new AssetMatcher(matcher);
            }
        }

        return assetMatcherDictionary;
    }

    private static IReadOnlyCollection<string> GetFiltersForDirectory(this IReadOnlyCollection<string> filters, string directory)
    {
        List<string> dirFilters = [];
        foreach (var filter in filters)
        {
            var filterParts = filter.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (filterParts.Length != 2)
            {
                throw new ArgumentException($"Invalid filter format: {filter}. Expected format is 'directory_pattern:asset_pattern'.");
            }

            var dirMatcher = new Matcher().AddInclude(filterParts[0]);
            if (!dirMatcher.Match(directory).HasMatches)
            {
                dirFilters.Add(filterParts[1]);
            }
        }
        return dirFilters;
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
}
