// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrInfo
{
    /// <summary>
    /// Path for temporary files (individual repo clones, created patches, etc.)
    /// </summary>
    LocalPath TmpPath { get; }

    /// <summary>
    /// Path to the root of the VMR
    /// </summary>
    LocalPath VmrPath { get; }

    /// <summary>
    /// Path within the VMR where VMR patches are stored.
    /// These patches are applied on top of the synchronized content.
    /// The Path is UNIX style and relative (e.g. "src/patches").
    /// </summary>
    string? PatchesPath { get; set; }

    /// <summary>
    /// Path to the source-mappings.json file 
    /// </summary>
    string? SourceMappingsPath { get; set; }

    /// <summary>
    /// Additionally mapped directories that are copied to non-src/ locations within the VMR.
    /// Paths are UNIX style and relative.
    /// Example: ("src/installer/eng/common", "eng/common")
    /// </summary>
    IReadOnlyCollection<(string Source, string? Destination)> AdditionalMappings { get; set; }

    /// <summary>
    /// Gets a full path leading to sources belonging to a given repo (mapping)
    /// </summary>
    LocalPath GetRepoSourcesPath(SourceMapping mapping);

    /// <summary>
    /// Gets a full path leading to sources belonging to a given repo
    /// </summary>
    LocalPath GetRepoSourcesPath(string mappingName);

    /// <summary>
    /// Gets a full path leading to the source manifest JSON file.
    /// </summary>
    LocalPath GetSourceManifestPath();
}

public class VmrInfo : IVmrInfo
{
    public const string SourcesDir = "src";
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string GitInfoSourcesDir = "prereqs/git-info";
    public const string SourceManifestFileName = "source-manifest.json";

    // These git attributes can override cloaking of files when set it individual repositories
    public const string KeepAttribute = "vmr-preserve";
    public const string IgnoreAttribute = "vmr-ignore";

    public const string ReadmeFileName = "README.md";
    public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";

    public static UnixPath RelativeSourcesDir { get; } = new("src");

    public LocalPath VmrPath { get; }

    public LocalPath TmpPath { get; }

    public string? PatchesPath { get; set; }

    public string? SourceMappingsPath { get; set; }

    public IReadOnlyCollection<(string Source, string? Destination)> AdditionalMappings { get; set; } = Array.Empty<(string, string?)>();

    public VmrInfo(LocalPath vmrPath, LocalPath tmpPath)
    {
        VmrPath = vmrPath;
        TmpPath = tmpPath;
    }

    public VmrInfo(string vmrPath, string tmpPath) : this(new NativePath(vmrPath), new NativePath(tmpPath))
    {
    }

    public LocalPath GetRepoSourcesPath(SourceMapping mapping) => GetRepoSourcesPath(mapping.Name);

    public LocalPath GetRepoSourcesPath(string mappingName) => VmrPath / SourcesDir / mappingName;

    public static UnixPath GetRelativeRepoSourcesPath(SourceMapping mapping) => RelativeSourcesDir / mapping.Name;

    public LocalPath GetSourceManifestPath() => VmrPath / SourcesDir / SourceManifestFileName;
}
