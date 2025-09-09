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

    /// <summary>
    /// Creates asset matchers for each target directory based on applicable filters.
    /// </summary>
    public static Dictionary<UnixPath, IAssetMatcher> GetAssetMatchersPerDirectory(this IReadOnlyCollection<string> filters, List<UnixPath> targetDirectories)
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
        
        // Normalize the directory - treat "." as root directory
        string normalizedDirectory = directory == "." ? string.Empty : directory;
        
        foreach (var filter in filters)
        {
            var lastSlash = filter.LastIndexOf('/');

            if (lastSlash == -1)
            {
                // No slash in filter - this is a root-level pattern
                if (string.IsNullOrEmpty(normalizedDirectory))
                {
                    dirFilters.Add(filter);
                }
                continue;
            }

            var filterPath = filter.Substring(0, lastSlash);
            var assetPattern = filter.Substring(lastSlash + 1);
            // If filterPath is "*" or "**", it applies to all directories
            if (filterPath == "*" || filterPath == "**")
            {
                dirFilters.Add(assetPattern);
                continue;
            }
            if (string.IsNullOrEmpty(normalizedDirectory))
            {
                continue;
            }
            // Use Matcher to check if the directory matches the filter path pattern
            var pathMatcher = new Matcher();
            pathMatcher.AddInclude(filterPath);

            // Check if this filter applies to the current directory using glob matching
            if (pathMatcher.Match(normalizedDirectory).HasMatches)
            {
                dirFilters.Add(assetPattern);
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
