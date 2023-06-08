// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;

namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

/// <summary>
/// Model for the git-info files that are part of the VMR.
/// These track what versions of individual repositories are included.
/// Example:
/// <![CDATA[
///    <Project>
///      <PropertyGroup>
///        <GitCommitHash>4ee620cc1b57da45d93135e064d43a83e65bbb6e</GitCommitHash>
///        <IsStable>false</IsStable>
///        <OfficialBuildId>20220803.1</OfficialBuildId>
///        <OutputPackageVersion>7.0.0-beta.22403.1</OutputPackageVersion>
///        <PreReleaseVersionLabel>beta</PreReleaseVersionLabel>
///      </PropertyGroup>
///    </Project>
/// ]]>
/// </summary>
public class GitInfoFile : MsBuildPropsFile
{
    public string GitCommitHash { get; set; }
    public string OfficialBuildId { get; set; }
    public string OutputPackageVersion { get; set; }
    public string PreReleaseVersionLabel { get; set; }
    public bool IsStable { get; set; }
    public int? GitCommitCount { get; set; }

    public GitInfoFile()
        : base(orderPropertiesAscending: null)
    {
    }

    protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
    {
        var properties = new Dictionary<string, string>
        {
            [nameof(GitCommitHash)] = GitCommitHash,
            [nameof(OfficialBuildId)] = OfficialBuildId,
            [nameof(OutputPackageVersion)] = OutputPackageVersion,
            [nameof(PreReleaseVersionLabel)] = PreReleaseVersionLabel,
            [nameof(IsStable)] = IsStable ? "true" : "false",
        };

        if (GitCommitCount.HasValue)
        {
            properties.Add(nameof(GitCommitCount), GitCommitCount.Value.ToString());
        }

        SerializeDictionary(properties, propertyGroup, createElement);
    }

    public static GitInfoFile DeserializeFromXml(string path)
    {
        var properties = DeserializeProperties(path);
        return new()
        {
            GitCommitHash = properties[nameof(GitCommitHash)],
            OfficialBuildId = properties[nameof(OfficialBuildId)],
            OutputPackageVersion = properties[nameof(OutputPackageVersion)],
            PreReleaseVersionLabel = properties[nameof(PreReleaseVersionLabel)],
            IsStable = properties[nameof(IsStable)] == "true",
            GitCommitCount = properties.TryGetValue(nameof(GitCommitCount), out var count) ? int.Parse(count) : null,
        };
    }
}

