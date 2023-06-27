// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NuGet.Versioning;
using System;

namespace Microsoft.DotNet.DarcLib;

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
    public const string NugetConfig = "NuGet.config";

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

    /// <summary>
    /// Reverse a version in the Arcade style (https://github.com/dotnet/arcade/blob/fb92b14d8cd07cf44f8f7eefa8ac58d7ffd05f3f/src/Microsoft.DotNet.Arcade.Sdk/tools/Version.BeforeCommonTargets.targets#L18)
    /// back to an OfficialBuildId + ReleaseLabel which we can then supply to get the same resulting version number.
    /// </summary>
    /// <param name="repoName">The source build name of the repo to get the version info for.</param>
    /// <param name="version">The complete version, e.g. 1.0.0-beta1-19720.5</param>
    public static (string BuildId, string ReleaseLabel) DeriveBuildInfo(string repoName, string version)
    {
        const string fallbackBuildIdFormat = "yyyyMMdd.1";

        var nugetVersion = new NuGetVersion(version);

        if (string.IsNullOrWhiteSpace(nugetVersion.Release))
        {
            // Finalized version number (x.y.z) - probably not our code
            // Application Insights, Newtonsoft.Json do this
            return (DateTime.Now.ToString(fallbackBuildIdFormat), string.Empty);
        }

        var releaseParts = nugetVersion.Release.Split('-', '.');
        if (repoName.Contains("nuget"))
        {
            // NuGet does this - arbitrary build IDs
            return (DateTime.Now.ToString(fallbackBuildIdFormat), releaseParts[0]);
        }

        if (releaseParts.Length == 3)
        {
            if (int.TryParse(releaseParts[1], out int datePart) && int.TryParse(releaseParts[2], out int buildPart))
            {
                if (datePart > 1 && datePart < 8 && buildPart > 1000 && buildPart < 10000)
                {
                    return (releaseParts[2], $"{releaseParts[0]}.{releaseParts[1]}");
                }
                    
                return (VersionToDate(datePart, buildPart), releaseParts[0]);
            }
        }

        if (releaseParts.Length == 4)
        {
            // New preview version style, e.g. 5.0.0-preview.7.20365.12
            if (int.TryParse(releaseParts[2], out int datePart) && int.TryParse(releaseParts[3], out int buildPart))
            {
                return (VersionToDate(datePart, buildPart), $"{releaseParts[0]}.{releaseParts[1]}");
            }
        }

        throw new FormatException($"Can't derive a build ID from version {version} (release {string.Join(";", nugetVersion.Release.Split('-', '.'))})");
    }

    private static string VersionToDate(int date, int build) => $"20{date / 1000}{date % 1000 / 50:D2}{date % 50:D2}.{build}";
}
