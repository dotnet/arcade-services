// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

/// <summary>
/// Asset matcher interface
/// </summary>
public interface IAssetMatcher
{
    // Determines whether a given asset name should be excluded from the update.
    bool IsExcluded(string name);

    /// <summary>
    /// Determines whether the asset should be excluded from the update, based on a
    /// name and optional relative path.
    /// </summary>
    /// <param name="name">Name of asset</param>
    /// <param name="relativePath">Relative path of the version details file (for VMRs) that is getting updated.</param>
    /// <returns>True if excluded, false otherwise.</returns>
    bool IsExcluded(string name, UnixPath relativePath);
}

public sealed class RepoOriginAssetMatcher : IAssetMatcher
{
    private readonly IReadOnlyList<string> _excludedOriginPatterns;
    private readonly IDictionary<string, string> _repoOriginToAssetMap;

    public RepoOriginAssetMatcher(IReadOnlyList<string> excludedOriginPatterns, IDictionary<string, string> repoOriginToAssetMap)
    {
        _excludedOriginPatterns = excludedOriginPatterns ?? throw new ArgumentNullException(nameof(excludedOriginPatterns));
        _repoOriginToAssetMap = repoOriginToAssetMap ?? throw new ArgumentNullException(nameof(repoOriginToAssetMap));
    }

    public bool IsExcluded(string name)
    {
        if (_excludedOriginPatterns.Count == 0)
        {
            return false;
        }

        // Find the repo origin for this asset
        if (!_repoOriginToAssetMap.TryGetValue(name, out string repoOrigin))
        {
            return false;
        }

        if (string.IsNullOrEmpty(repoOrigin))
        {
            return false;
        }

        return _excludedOriginPatterns.Any(p => repoOrigin.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsExcluded(string name, UnixPath relativePath) => IsExcluded(name);
}

public class NameBasedAssetMatcher : IAssetMatcher
{
    private readonly Matcher _matcher;

    public NameBasedAssetMatcher(IReadOnlyCollection<string> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            _matcher = null;
            return;
        }
        _matcher = new Matcher();
        _matcher.AddIncludePatterns(filters);
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
