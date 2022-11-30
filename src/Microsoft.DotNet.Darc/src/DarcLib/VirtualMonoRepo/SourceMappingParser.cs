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
    Task<IReadOnlyCollection<SourceMapping>> ParseMappings();
}

/// <summary>
/// Class responsible for parsing the source-mappings.json file.
/// More details about source-mappings.json are directly in the file or at
/// https://github.com/dotnet/arcade/blob/main/Documentation/UnifiedBuild/VMR-Design-And-Operation.md#repository-source-mappings
/// </summary>
public class SourceMappingParser : ISourceMappingParser
{
    private readonly IVmrInfo _vmrInfo;

    public SourceMappingParser(IVmrInfo vmrInfo)
    {
        _vmrInfo = vmrInfo;
    }

    public async Task<IReadOnlyCollection<SourceMapping>> ParseMappings()
    {
        var mappingFilePath = _vmrInfo.VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName;
        var mappingFile = new FileInfo(mappingFilePath);

        if (!mappingFile.Exists)
        {
            throw new FileNotFoundException(
                $"Failed to find {VmrInfo.SourceMappingsFileName} file in the VMR directory",
                mappingFilePath);
        }

        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        using var stream = File.Open(mappingFile.FullName, FileMode.Open);
        var settings = await JsonSerializer.DeserializeAsync<SourceMappingFile>(stream, options)
            ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

        _vmrInfo.PatchesPath = NormalizePath(settings.PatchesPath);

        if (settings.AdditionalMappings is not null)
        {
            var additionalMappings = new List<(string Source, string? Destination)>();
            foreach (var additionalMapping in settings.AdditionalMappings)
            {
                if (additionalMapping.Source is null || NormalizePath(additionalMapping.Source) is null || !additionalMapping.Source.StartsWith($"{VmrInfo.SourcesDir}/"))
                {
                    throw new Exception($"Additional mapping for {additionalMapping.Destination} needs to declare the source pointing to {VmrInfo.SourcesDir}/");
                }

                additionalMappings.Add((additionalMapping.Source, NormalizePath(additionalMapping.Destination)));
            }

            _vmrInfo.AdditionalMappings = additionalMappings.ToImmutableArray();
        }

        var mappings = settings.Mappings
            .Select(mapping => CreateMapping(settings.Defaults, mapping))
            .ToImmutableArray();

        return mappings;
    }

    private static SourceMapping CreateMapping(SourceMappingSetting defaults, SourceMappingSetting setting)
    {
        if (setting.Name is null)
        {
            throw new InvalidOperationException(
                $"Missing `{nameof(SourceMapping.Name).ToLower()}` in {VmrInfo.SourceMappingsFileName}");
        }

        if (setting.DefaultRemote is null)
        {
            throw new InvalidOperationException(
                $"Missing `{nameof(SourceMapping.DefaultRemote).ToLower()}` in {VmrInfo.SourceMappingsFileName}");
        }

        IEnumerable<string> include = setting.Include ?? Enumerable.Empty<string>();
        IEnumerable<string> exclude = setting.Exclude ?? Enumerable.Empty<string>();

        if (!setting.IgnoreDefaults)
        {
            if (defaults.Include is not null)
            {
                include = defaults.Include.Concat(include).ToArray();
            }

            if (defaults.Exclude is not null)
            {
                exclude = defaults.Exclude.Concat(exclude).ToArray();
            }
        }

        return new SourceMapping(
            Name: setting.Name,
            Version: setting.Version,
            DefaultRemote: setting.DefaultRemote,
            DefaultRef: setting.DefaultRef ?? defaults.DefaultRef ?? "main",
            Include: include.ToImmutableArray(),
            Exclude: exclude.ToImmutableArray());
    }

    private static string? NormalizePath(string? relativePath)
    {
        if (relativePath is null || relativePath == "." || relativePath == "/" || relativePath.Length == 0)
        {
            return null;
        }

        if (relativePath.Contains('\\') || relativePath.StartsWith('/'))
        {
            throw new Exception($"Invalid value '{relativePath}' in {VmrInfo.SourceMappingsFileName}. " +
                $"The path must be relative to the VMR directory and use UNIX directory separators (e.g. src/installer/patches).");
        }

        return relativePath;
    }

    private class SourceMappingFile
    {
        public SourceMappingSetting Defaults { get; set; } = new()
        {
            DefaultRef = "main",
            Include = Array.Empty<string>(),
            Exclude = Array.Empty<string>(),
        };

        public string? PatchesPath { get; set; }

        public List<SourceMappingSetting> Mappings { get; set; } = new();

        public List<AdditionalMappingSetting>? AdditionalMappings { get; set; }
    }

    private class SourceMappingSetting
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? DefaultRemote { get; set; }
        public string? DefaultRef { get; set; }
        public string[]? Include { get; set; }
        public string[]? Exclude { get; set; }
        public bool IgnoreDefaults { get; set; }
    }

    private class AdditionalMappingSetting
    {
        public string? Source { get; set; }
        public string? Destination { get; set; }
    }
}
