// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ISourceMappingParser
{
    Task<IReadOnlyCollection<SourceMapping>> ParseMappings(string path);
}

public class SourceMappingParser : ISourceMappingParser
{
    private const string SourceMappingsFileName = "source-mappings.json";

    public async Task<IReadOnlyCollection<SourceMapping>> ParseMappings(string vmrPath)
    {
        var mappingFilePath = Path.Combine(vmrPath, "src", SourceMappingsFileName);
        var mappingFile = new FileInfo(mappingFilePath);

        if (!mappingFile.Exists)
        {
            throw new FileNotFoundException(
                $"Failed to find {SourceMappingsFileName} file in the VMR directory",
                mappingFilePath);
        }

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        using var stream = File.Open(mappingFile.FullName, FileMode.Open);
        var mappings = await JsonSerializer.DeserializeAsync<SourceMappingSetting[]>(stream, options)
            ?? throw new Exception($"Failed to deserialize {SourceMappingsFileName}");

        return mappings
            .Select(m => new SourceMapping(
                Name: m.Name ?? throw new InvalidOperationException($"Missing `name` in {SourceMappingsFileName}"),
                Version: m.Version,
                DefaultRemote: m.DefaultRemote ?? throw new InvalidOperationException($"Missing `defaultRemote` in {SourceMappingsFileName}"),
                DefaultRef: m.DefaultRef ?? "main",
                Include: ImmutableArray.Create(m.Include ?? Array.Empty<string>()),
                Exclude: ImmutableArray.Create(m.Exclude ?? Array.Empty<string>())))
            .ToImmutableArray();
    }

    private class SourceMappingSetting
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? DefaultRemote { get; set; }
        public string? DefaultRef { get; set; }
        public string[]? Include { get; set; }
        public string[]? Exclude { get; set; }
    }
}
