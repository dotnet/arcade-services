// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

/// <summary>
/// Representation of the whole git-info file structure.
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
    public GitInfoFileItem Item { get; }

    public GitInfoFile(GitInfoFileItem item)
    {
        Item = item;
    }

    protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
    {
        var properties = new Dictionary<string, string>
        {
            [nameof(GitInfoFileItem.GitCommitHash)] = Item.GitCommitHash,
            [nameof(GitInfoFileItem.OfficialBuildId)] = Item.OfficialBuildId,
            [nameof(GitInfoFileItem.OutputPackageVersion)] = Item.OutputPackageVersion,
            [nameof(GitInfoFileItem.PreReleaseVersionLabel)] = Item.PreReleaseVersionLabel,
            [nameof(GitInfoFileItem.IsStable)] = string.IsNullOrWhiteSpace(Item.PreReleaseVersionLabel) ? "true" : "false",
        };

        if (Item.GitCommitCount.HasValue)
        {
            properties.Add(nameof(GitInfoFileItem.GitCommitCount), Item.GitCommitCount.Value.ToString());
        }

        SerializeDictionary(properties, propertyGroup, createElement);
    }
}

/// <summary>
/// Item representing information about a repo stored in a "git-info" XML file in the VMR.
/// </summary>
#nullable disable
public class GitInfoFileItem
{
    public string GitCommitHash { get; set; }
    public string OfficialBuildId { get; set; }
    public string OutputPackageVersion { get; set; }
    public string PreReleaseVersionLabel { get; set; }
    public bool IsStable { get; set; }
    public int? GitCommitCount { get; set; }
}
