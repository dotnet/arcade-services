// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Xml;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public interface IAllVersionsPropsFile : IMsBuildPropsFile
{
    Dictionary<string, string> Versions { get; }

    void UpdateVersion(string repository, string sha);

    bool DeleteVersion(string repository);
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

    public AllVersionsPropsFile(IReadOnlyCollection<ISourceComponent> repositoryRecords)
        : base(orderPropertiesAscending: true)
    {
        Versions = [];
        foreach (var repo in repositoryRecords)
        {
            UpdateVersion(repo.Path, repo.CommitSha);
        }
    }

    public void UpdateVersion(string repository, string sha)
    {
        var key = SanitizePropertyName(repository);
        Versions[key + ShaPropertyName] = sha;
    }

    public bool DeleteVersion(string repository)
    {
        var key = SanitizePropertyName(repository);
        var deleted = Versions.Remove(key + ShaPropertyName);
        return deleted;
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
