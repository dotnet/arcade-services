// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class VmrManager
{
    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrManager(IReadOnlyCollection<SourceMapping> mappings)
    {
        Mappings = mappings;
    }

    public static VmrManager FromPath(string path)
    {
        var mappingFilePath = new FileInfo(Path.Combine(path, "src", "source-mappings.json"));

        if (!mappingFilePath.Exists)
        {
            throw new FileNotFoundException(
                "Failed to find source-mappings.json file in the VMR directory",
                mappingFilePath.FullName);
        }

        var mappings = JsonSerializer.Deserialize<SourceMappingSetting[]>(mappingFilePath.FullName)
                ?? throw new Exception("Failed to deserialize source-mappings.json");

        return new VmrManager(mappings.Select(m => new SourceMapping(
            Name: m.Name ?? throw new InvalidOperationException("Missing `name` in source-mappings.json"),
            Version: m.Version,
            DefaultRemote: m.DefaultRemote ?? throw new InvalidOperationException("Missing `defaultRemote` in source-mappings.json"),
            DefaultBranch: m.DefaultBranch,
            Include: ImmutableArray.Create(m.Include ?? Array.Empty<string>()),
            Exclude: ImmutableArray.Create(m.Exclude ?? Array.Empty<string>()))).ToImmutableArray());
    }

    private class SourceMappingSetting
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? DefaultRemote { get; set; }
        public string? DefaultBranch { get; set; }
        public string[]? Include { get; set; }
        public string[]? Exclude { get; set; }
    }
}
