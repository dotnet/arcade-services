// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IVmrInfo
{
    /// <summary>
    /// Path for temporary files (individual repo clones, created patches, etc.)
    /// </summary>
    NativePath TmpPath { get; set; }

    /// <summary>
    /// Path to the root of the VMR
    /// </summary>
    NativePath VmrPath { get; set; }

    /// <summary>
    /// Uri from which the VMR has been cloned
    /// </summary>
    string VmrUri { get; set; }

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
    /// Path to the third-party notices template file
    /// </summary>
    string? ThirdPartyNoticesTemplatePath { get; set; }

    /// <summary>
    /// Gets a full path leading to the source manifest JSON file.
    /// </summary>
    NativePath SourceManifestPath { get; }

    /// <summary>
    /// Additionally mapped directories that are copied to non-src/ locations within the VMR.
    /// Paths are UNIX style and relative.
    /// Example: ("src/installer/eng/common", "eng/common")
    /// </summary>
    IReadOnlyCollection<(string Source, string? Destination)> AdditionalMappings { get; set; }

    /// <summary>
    /// Gets a full path leading to sources belonging to a given repo (mapping)
    /// </summary>
    NativePath GetRepoSourcesPath(SourceMapping mapping);

    /// <summary>
    /// Gets a full path leading to sources belonging to a given repo
    /// </summary>
    NativePath GetRepoSourcesPath(string mappingName);

    string? ThirdPartyNoticesTemplateFullPath { get; }
}

public class VmrInfo : IVmrInfo
{
    public static readonly UnixPath SourcesDir = new(SourceDirName);
    public static readonly UnixPath CodeownersPath = new(".github/" + CodeownersFileName);
    public static readonly UnixPath CredScanSuppressionsPath = new(".config/" + CredScanSuppressionsFileName);

    public const string SourceDirName = "src";
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string SourceManifestFileName = "source-manifest.json";

    // These git attributes can override cloaking of files when set it individual repositories
    public const string KeepAttribute = "vmr-preserve";
    public const string IgnoreAttribute = "vmr-ignore";

    public const string ThirdPartyNoticesFileName = "THIRD-PARTY-NOTICES.txt";
    public const string CodeownersFileName = "CODEOWNERS";
    public const string CredScanSuppressionsFileName = "CredScanSuppressions.json";

    public const string ArcadeMappingName = "arcade";
    public static readonly UnixPath ArcadeRepoDir = SourcesDir / ArcadeMappingName;

    public static UnixPath DefaultRelativeSourceMappingsPath { get; } = SourcesDir / SourceMappingsFileName;

    public static UnixPath DefaultRelativeSourceManifestPath { get; } = SourcesDir / SourceManifestFileName;

    private NativePath _vmrPath;

    public NativePath VmrPath
    {
        get
        {
            return _vmrPath;
        }

        set
        {
            _vmrPath = value;
            SourceManifestPath = value / DefaultRelativeSourceManifestPath;
        }
    }

    public NativePath TmpPath { get; set; }

    public string? PatchesPath { get; set; }

    public string? SourceMappingsPath { get; set; }

    public string? ThirdPartyNoticesTemplatePath { get; set; }

    public string VmrUri { get; set; }

    public IReadOnlyCollection<(string Source, string? Destination)> AdditionalMappings { get; set; } = Array.Empty<(string, string?)>();

    public VmrInfo(NativePath vmrPath, NativePath tmpPath)
    {
        _vmrPath = vmrPath;
        TmpPath = tmpPath;
        SourceManifestPath = vmrPath / SourcesDir / SourceManifestFileName;
        VmrUri = Constants.DefaultVmrUri;
    }

    public VmrInfo(string vmrPath, string tmpPath) : this(new NativePath(vmrPath), new NativePath(tmpPath))
    {
    }

    public NativePath GetRepoSourcesPath(SourceMapping mapping) => GetRepoSourcesPath(mapping.Name);

    public NativePath GetRepoSourcesPath(string mappingName) => VmrPath / SourcesDir / mappingName;

    public static UnixPath GetRelativeRepoSourcesPath(SourceMapping mapping) => GetRelativeRepoSourcesPath(mapping.Name);

    public static UnixPath GetRelativeRepoSourcesPath(string mappingName) => SourcesDir / mappingName;
    public string? ThirdPartyNoticesTemplateFullPath =>
        string.IsNullOrEmpty(ThirdPartyNoticesTemplatePath)
            ? null
            : (_vmrPath / ThirdPartyNoticesTemplatePath).ToString();

    public NativePath SourceManifestPath { get; private set; }
}
