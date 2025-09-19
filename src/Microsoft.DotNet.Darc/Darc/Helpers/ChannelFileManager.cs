// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.ProductConstructionService.Client.Helpers;

namespace Microsoft.DotNet.Darc.Helpers;

/// <summary>
/// Helper class for managing channel categorization and file organization.
/// Based on the categorization logic used in ChannelCategorizer.
/// </summary>
public static class ChannelFileManager
{
    /// <summary>
    /// Determines the category for a given channel based on its name and classification.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="classification">The classification of the channel</param>
    /// <returns>The category name for the channel</returns>
    public static string DetermineCategoryForChannel(string channelName, string classification)
    {
        // Handle test channels first
        if (string.Equals(classification, "test", StringComparison.OrdinalIgnoreCase))
        {
            return "Test";
        }

        // Check if channel name starts with any of the category names
        foreach (var category in ChannelCategorizer.CategoryNames)
        {
            if (channelName.StartsWith(category, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        // Default to "Other" if no match found
        return "Other";
    }

    /// <summary>
    /// Gets the file path for a given category.
    /// </summary>
    /// <param name="category">The category name</param>
    /// <returns>Unix path to the category file</returns>
    public static UnixPath GetCategoryFilePath(string category)
    {
        string safeFileName = SanitizeFileName(category);
        return new UnixPath($"channels/{safeFileName}.yml");
    }

    /// <summary>
    /// Gets all possible category file paths.
    /// </summary>
    /// <returns>Collection of all category file paths</returns>
    public static IEnumerable<UnixPath> GetAllCategoryFilePaths()
    {
        return ChannelCategorizer.CategoryNames.Select(GetCategoryFilePath);
    }

    /// <summary>
    /// Sanitizes a category name to be safe for use as a filename.
    /// </summary>
    /// <param name="categoryName">The category name to sanitize</param>
    /// <returns>A safe filename</returns>
    private static string SanitizeFileName(string categoryName)
    {
        // Replace characters that are not safe for filenames
        string sanitized = categoryName
            .Replace(" ", "-")
            .Replace(".", "dot")
            .ToLowerInvariant();

        // Handle special cases for readability
        if (sanitized.StartsWith("dotnet-"))
        {
            // .NET 6 becomes dotnet-6.yml, .NET becomes dotnet.yml
            return sanitized;
        }

        return sanitized;
    }
}
