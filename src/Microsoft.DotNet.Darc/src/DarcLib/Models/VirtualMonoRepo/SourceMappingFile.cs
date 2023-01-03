// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public class SourceMappingFile
{
    public SourceMappingSetting Defaults { get; set; } = new()
    {
        DefaultRef = "main",
        Include = Array.Empty<string>(),
        Exclude = Array.Empty<string>(),
    };

    public string? PatchesPath { get; set; }

    public string? SourceMappingsPath { get; set; }

    public List<SourceMappingSetting> Mappings { get; set; } = new();

    public List<AdditionalMappingSetting>? AdditionalMappings { get; set; }
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
}

public class AdditionalMappingSetting
{
    public string? Source { get; set; }
    public string? Destination { get; set; }
}
