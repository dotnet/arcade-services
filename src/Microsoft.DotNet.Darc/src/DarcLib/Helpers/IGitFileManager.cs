// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

public interface IGitFileManager
{
    /// <summary>
    /// Add a new dependency to the repository
    /// </summary>
    /// <param name="dependency">Dependency to add.</param>
    /// <param name="repoUri">Repository URI to add the dependency to.</param>
    /// <param name="branch">Branch to add the dependency to.</param>
    /// <returns>Async task.</returns>
    Task AddDependencyAsync(DependencyDetail dependency, string repoUri, string branch);
    Task AddDependencyToGlobalJson(string repo, string branch, string parentField, string dependencyName, string version);
    Task AddDependencyToVersionDetailsAsync(string repo, string branch, DependencyDetail dependency);

    /// <summary>
    ///     <!-- Package versions -->
    ///     <PropertyGroup>
    ///         <MicrosoftDotNetApiCompatPackageVersion>1.0.0-beta.18478.5</MicrosoftDotNetApiCompatPackageVersion>
    ///     </PropertyGroup>
    ///
    ///     See https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md for more
    ///     information.
    /// </summary>
    /// <param name="repo">Path to Versions.props file</param>
    /// <param name="dependency">Dependency information to add.</param>
    /// <returns>Async task.</returns>
    Task AddDependencyToVersionsPropsAsync(string repo, string branch, DependencyDetail dependency);

    /// <summary>
    ///  Infer repo names from feeds using regex.
    ///  If any feed name resolution fails, we'll just put it into an "unknown" bucket.
    /// </summary>
    /// <param name="assetLocationMap">Dictionary of all feeds by their location</param>
    /// <returns>Dictionary with key = repo name for logging, value = hashset of feeds</returns>
    Dictionary<string, HashSet<string>> FlattenLocationsAndSplitIntoGroups(Dictionary<string, HashSet<string>> assetLocationMap);
    List<(string key, string feed)> GetPackageSources(XmlDocument nugetConfig, Func<string, bool> filter = null);
    Task<IEnumerable<DependencyDetail>> ParseVersionDetailsXmlAsync(string repoUri, string branch, bool includePinned = true);
    Task<JObject> ReadDotNetToolsConfigJsonAsync(string repoUri, string branch);
    Task<JObject> ReadGlobalJsonAsync(string repoUri, string branch);
    Task<XmlDocument> ReadNugetConfigAsync(string repoUri, string branch);
    Task<XmlDocument> ReadVersionDetailsXmlAsync(string repoUri, string branch);
    Task<XmlDocument> ReadVersionPropsAsync(string repoUri, string branch);
    Task<GitFileContentContainer> UpdateDependencyFiles(IEnumerable<DependencyDetail> itemsToUpdate, string repoUri, string branch, IEnumerable<DependencyDetail> oldDependencies, SemanticVersion incomingDotNetSdkVersion);
    XmlDocument UpdatePackageSources(XmlDocument nugetConfig, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo);

    /// <summary>
    ///     Verify the local repository has correct and consistent dependency information.
    ///     Currently, this implementation checks:
    ///     - global.json, Version.props and Version.Details.xml can be parsed.
    ///     - There are no duplicated dependencies in Version.Details.xml
    ///     - If a dependency exists in Version.Details.xml and in version.props/global.json, the versions match.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branch"></param>
    /// <returns>Async task</returns>
    Task<bool> Verify(string repo, string branch);

    /// <summary>
    ///     Ensure that there is a unique propertyName + condition on the list.
    /// </summary>
    /// <param name="versionProps">Xml object representing MSBuild properties file.</param>
    /// <returns>True if there are no duplicated properties.</returns>
    Task<bool> VerifyNoDuplicatedProperties(XmlDocument versionProps);
}
