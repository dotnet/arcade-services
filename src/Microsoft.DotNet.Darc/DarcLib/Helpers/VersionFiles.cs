// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
///     Generic helpers for dealing with version files.
/// </summary>
public static class VersionFiles
{
    // Locations of the version files within a repository
    public const string VersionDetailsXml = "eng/Version.Details.xml";
    public const string VersionProps = "eng/Versions.props";
    public const string GlobalJson = "global.json";
    public const string DotnetToolsConfigJson = ".config/dotnet-tools.json";
    public static readonly IReadOnlyCollection<string> NugetConfigNames = ["NuGet.config", "nuget.config", "NuGet.Config"];

    private static string GetVersionPropsElementBaseName(string dependencyName)
    {
        // Remove characters which appear in package names that we don't want in msbuild property names
        return dependencyName.Replace(".", string.Empty).Replace("-", string.Empty);
    }

    /// <summary>
    ///     Determine the Versions.props version element name for a specific dependency.
    /// </summary>
    /// <param name="dependencyName">Dependency</param>
    /// <returns>Element name</returns>
    public static string GetVersionPropsPackageVersionElementName(string dependencyName)
    {
        return $"{GetVersionPropsElementBaseName(dependencyName)}{VersionDetailsParser.VersionPropsVersionElementSuffix}";
    }

    /// <summary>
    /// Special temporary alternative package version element names.  This is used where the
    /// version props file already has "Version" instead of PackageVersion. Eventually this will
    /// be replaced by use of configuration in Versions.Details.xml
    /// </summary>
    /// <param name="dependencyName">Original name of dependency</param>
    /// <returns></returns>
    public static string GetVersionPropsAlternatePackageVersionElementName(string dependencyName)
    {
        return $"{GetVersionPropsElementBaseName(dependencyName)}{VersionDetailsParser.VersionPropsAlternateVersionElementSuffix}";
    }

    public static string CalculateGlobalJsonElementName(string dependencyName)
    {
        return dependencyName;
    }

    public static string CalculateDotnetToolsJsonElementName(string dependencyName)
    {
        return dependencyName;
    }
}
