// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

public interface IAllVersionsPropsFile : IMsBuildPropsFile
{
    Dictionary<string, string> Versions { get; }

    VmrDependencyVersion? GetVersion(string repository);
    void UpdateVersion(string repository, VmrDependencyVersion version);
}

/// <summary>
/// A model for a file AllRepoVersions.props which is part of the VMR and contains list of all versions
/// of all synchronized individual repositories.
/// </summary>
public class AllVersionsPropsFile : MsBuildPropsFile, IAllVersionsPropsFile
{
    public const string FileName = "AllRepoVersions.props";
    private const string ShaPropertyName = "GitCommitHash";
    private const string PackageVersionPropertyName = "OutputPackageVersion";

    public Dictionary<string, string> Versions { get; }

    public AllVersionsPropsFile(Dictionary<string, string> versions)
        : base(orderPropertiesAscending: true)
    {
        Versions = versions;
    }

    public VmrDependencyVersion? GetVersion(string repository)
    {
        var key = SanitizePropertyName(repository) + ShaPropertyName;
        Versions.TryGetValue(key, out var sha);

        if (sha is null)
        {
            return null;
        }

        key = SanitizePropertyName(repository) + PackageVersionPropertyName;
        Versions.TryGetValue(key, out var version);

        return new(sha, version);
    }

    public void UpdateVersion(string repository, VmrDependencyVersion version)
    {
        var key = SanitizePropertyName(repository);

        if (version.Sha is not null)
        {
            Versions[key + ShaPropertyName] = version.Sha;
        }
        else if (Versions.ContainsKey(key + ShaPropertyName))
        {
            Versions.Remove(key + ShaPropertyName);
        }

        if (version.PackageVersion is not null)
        {
            Versions[key + PackageVersionPropertyName] = version.PackageVersion;
        }
        else if (Versions.ContainsKey(key + PackageVersionPropertyName))
        {
            Versions.Remove(key + PackageVersionPropertyName);
        }
    }

    public static AllVersionsPropsFile DeserializeFromXml(string path)
    {
        var versions = DeserializeProperties(path);
        return new AllVersionsPropsFile(versions);
    }

    protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
        => SerializeDictionary(Versions, propertyGroup, createElement);

    private static string SanitizePropertyName(string propertyName) => propertyName
        .Replace("-", string.Empty)
        .Replace(".", string.Empty);
}
