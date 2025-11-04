// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

/// <summary>
/// A model for the source-mappings.json file that configures where the VMR synchronizes the sources from.
/// Each development repository has a mapping record which says where the remote repo is,
/// what files are in/excluded from the sync, etc.
public class SourceMappingFile
{
    /// <summary>
    /// The Defaults are added to all mappings unless `ignoreDefaults: true` is specified
    /// When no "include" filter is specified, "**/*" is used
    /// The default filters do not apply to submodules
    /// Only filters which start with submodule's path are applied when syncing submodules
    /// </summary>
    public SourceMappingSetting Defaults { get; set; } = new()
    {
        DefaultRef = "main",
        Include = [],
        Exclude = [],
    };

    /// <summary>
    /// Location within the VMR where the third-party notices template file is stored
    /// </summary>
    public string? ThirdPartyNoticesTemplatePath { get; set; }

    /// <summary>
    /// Each of these mappings has a corresponding folder in the src/ directory
    /// </summary>
    public List<SourceMappingSetting> Mappings { get; set; } = [];
}

public class SourceMappingSetting
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? DefaultRemote { get; set; }
    public string? DefaultRef { get; set; }
    public string[]? Include { get; set; }
    public string[]? Exclude { get; set; }
    public bool IgnoreDefaults { get; set; }
    public bool DisableSynchronization { get; set; }
}

public class AdditionalMappingSetting
{
    public string? Source { get; set; }
    public string? Destination { get; set; }
}
