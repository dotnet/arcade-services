// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

/// <summary>
/// Represents various actions around files in git that can be performed on a local repository or even remotely.
/// Most of these are connected to the Maestro dependency flow system.
/// </summary>
public interface IDependencyFileManager
{
    Task AddDependencyAsync(
        DependencyDetail dependency,
        string repoUri,
        string? branch,
        string? mapping = null,
        bool versionDetailsOnly = false,
        bool? repoHasVersionDetailsProps = null);

    Task RemoveDependencyAsync(string dependencyName, string repoUri, string branch, string? mapping = null, bool? repoHasVersionDetailsProps = null);

    Dictionary<string, HashSet<string>> FlattenLocationsAndSplitIntoGroups(Dictionary<string, HashSet<string>> assetLocationMap);

    List<(string key, string feed)> GetPackageSources(XmlDocument nugetConfig, Func<string, bool>? filter = null);

    Task<VersionDetails> ParseVersionDetailsXmlAsync(string repoUri, string branch, bool includePinned = true, string? mapping = null);

    Task<JObject> ReadDotNetToolsConfigJsonAsync(string repoUri, string branch, string? mapping = null);

    /// <summary>
    /// Get the tools.dotnet section of the global.json from a target repo URI
    /// </summary>
    /// <param name="repoUri">repo to get the version from</param>
    /// <param name="commit">commit sha to query</param>
    Task<SemanticVersion> ReadToolsDotnetVersionAsync(string repoUri, string commit, string? mapping = null);

    Task<JObject> ReadGlobalJsonAsync(string repoUri, string branch, string? mapping = null);

    Task<(string Name, XmlDocument Content)> ReadNugetConfigAsync(string repoUri, string branch);

    Task<XmlDocument> ReadVersionDetailsXmlAsync(string repoUri, string branch, string? mapping = null);

    Task<XmlDocument> ReadVersionPropsAsync(string repoUri, string branch, string? mapping = null);

    Task<GitFileContentContainer> UpdateDependencyFiles(
        IEnumerable<DependencyDetail> itemsToUpdate,
        SourceDependency? sourceDependency,
        string repoUri,
        string? branch,
        IEnumerable<DependencyDetail> oldDependencies,
        SemanticVersion? incomingDotNetSdkVersion,
        bool forceGlobalJsonUpdate = false,
        bool? repoHasVersionDetailsProps = null);

    XmlDocument UpdatePackageSources(XmlDocument nugetConfig, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo);

    Task<bool> Verify(string repo, string branch);

    Task<bool> VerifyNoDuplicatedProperties(XmlDocument versionProps);

    Task<bool> VersionDetailsPropsExistsAsync(string repoUri, string branch, string? mapping = null);
}
