// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Represents various actions around files in git that can be performed on a local repository or even remotely.
/// Most of these are connected to the Maestro dependency flow system.
/// </summary>
public interface IDependencyFileManager
{
    Task AddDependencyAsync(DependencyDetail dependency, string repoUri, string branch);

    Dictionary<string, HashSet<string>> FlattenLocationsAndSplitIntoGroups(Dictionary<string, HashSet<string>> assetLocationMap);

    List<(string key, string feed)> GetPackageSources(XmlDocument nugetConfig, Func<string, bool> filter = null);

    Task<IEnumerable<DependencyDetail>> ParseVersionDetailsXmlAsync(string repoUri, string branch, bool includePinned = true);

    Task<JObject> ReadDotNetToolsConfigJsonAsync(string repoUri, string branch);

    Task<JObject> ReadGlobalJsonAsync(string repoUri, string branch);

    Task<XmlDocument> ReadNugetConfigAsync(string repoUri, string branch);

    Task<XmlDocument> ReadVersionDetailsXmlAsync(string repoUri, string branch);

    Task<XmlDocument> ReadVersionPropsAsync(string repoUri, string branch);

    Task<GitFileContentContainer> UpdateDependencyFiles(
        IEnumerable<DependencyDetail> itemsToUpdate,
        string repoUri,
        string branch,
        IEnumerable<DependencyDetail> oldDependencies,
        SemanticVersion incomingDotNetSdkVersion);

    XmlDocument UpdatePackageSources(XmlDocument nugetConfig, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo);

    Task<bool> Verify(string repo, string branch);

    Task<bool> VerifyNoDuplicatedProperties(XmlDocument versionProps);
}
