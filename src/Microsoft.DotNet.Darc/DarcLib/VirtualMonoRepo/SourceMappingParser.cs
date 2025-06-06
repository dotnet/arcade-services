// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ISourceMappingParser
{
    Task<IReadOnlyCollection<SourceMapping>> ParseMappings(string mappingFilePath);
    IReadOnlyCollection<SourceMapping> ParseMappingsFromJson(string json);
}

/// <summary>
/// Class responsible for parsing the source-mappings.json file.
/// More details about source-mappings.json are directly in the file or at
/// https://github.com/dotnet/dotnet/tree/main/docs/VMR-Design-And-Operation.md#repository-source-mappings
/// </summary>
public class SourceMappingParser : ISourceMappingParser
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;

    public SourceMappingParser(IVmrInfo vmrInfo, IFileSystem fileSystem)
    {
        _vmrInfo = vmrInfo;
        _fileSystem = fileSystem;
    }

    public async Task<IReadOnlyCollection<SourceMapping>> ParseMappings(string mappingFilePath)
    {
        return ParseMappingsFromJson(await _fileSystem.ReadAllTextAsync(mappingFilePath));
    }

    public IReadOnlyCollection<SourceMapping> ParseMappingsFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        var settings = JsonSerializer.Deserialize<SourceMappingFile>(json, options)
            ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

        _vmrInfo.PatchesPath = NormalizePath(settings.PatchesPath);
        _vmrInfo.SourceMappingsPath = settings.SourceMappingsPath;
        _vmrInfo.ThirdPartyNoticesTemplatePath = settings.ThirdPartyNoticesTemplatePath;

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
            Exclude: exclude.ToImmutableArray(),
            DisableSynchronization: setting.DisableSynchronization);
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
}
