// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICredScanSuppressionsGenerator
{
    Task UpdateCredScanSuppressions(CancellationToken cancellationToken);
}

public class CredScanSuppressionsGenerator : ICredScanSuppressionsGenerator
{
    private static readonly IReadOnlyCollection<LocalPath> s_credScanSuppressionsLocations = new[]
    {
        new UnixPath(".config/" + VmrInfo.CredScanSuppressionsFileName),
        new UnixPath("eng/" + VmrInfo.CredScanSuppressionsFileName),
    };

    private readonly IVmrInfo _vmrInfo;
    private readonly ISourceManifest _sourceManifest;
    private readonly ILocalGitClient _localGitClient;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<CredScanSuppressionsGenerator> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CredScanSuppressionsGenerator(
        IVmrInfo vmrInfo,
        ISourceManifest sourceManifest,
        ILocalGitClient localGitClient,
        IFileSystem fileSystem,
        ILogger<CredScanSuppressionsGenerator> logger)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _localGitClient = localGitClient;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Generates the CredScanSuppressions.json file by gathering individual repo CredScanSuppressions.json files.
    /// </summary>
    public async Task UpdateCredScanSuppressions(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating {credscansuppressions}...", VmrInfo.CredScanSuppressionsPath);

        var destPath = _vmrInfo.VmrPath / VmrInfo.CredScanSuppressionsPath;

        CredScanSuppressionFile vmrCredScanSuppressionsFile = new CredScanSuppressionFile();

        _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(destPath)
            ?? throw new Exception($"Failed to create {VmrInfo.CredScanSuppressionsFileName} in {destPath}"));

        bool fileExistedBefore = _fileSystem.FileExists(destPath);

        using (var destStream = _fileSystem.GetFileStream(destPath, FileMode.Create, FileAccess.Write))
        {
            foreach (ISourceComponent component in _sourceManifest.Repositories.OrderBy(m => m.Path))
            {
                await AddCredScanSuppressionsContent(vmrCredScanSuppressionsFile, component.Path, cancellationToken);

                foreach (var submodule in _sourceManifest.Submodules.Where(s => s.Path.StartsWith($"{component.Path}/")))
                {
                    await AddCredScanSuppressionsContent(vmrCredScanSuppressionsFile, submodule.Path, cancellationToken);
                }
            }

            JsonSerializer.Serialize(destStream, vmrCredScanSuppressionsFile, _jsonOptions);
        }

        if (vmrCredScanSuppressionsFile.Suppressions.Count == 0)
        {
            _fileSystem.DeleteFile(destPath);
        }

        bool fileExistsAfter = _fileSystem.FileExists(destPath);

        if (fileExistsAfter || fileExistedBefore)
        {
            await _localGitClient.StageAsync(_vmrInfo.VmrPath, new string[] { VmrInfo.CredScanSuppressionsPath }, cancellationToken);
        }

        _logger.LogInformation("{credscansuppressions} updated", VmrInfo.CredScanSuppressionsPath);
    }

    private async Task AddCredScanSuppressionsContent(CredScanSuppressionFile vmrCredScanSuppressionsFile, string repoPath, CancellationToken cancellationToken)
    {
        foreach (var location in s_credScanSuppressionsLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var repoCredScanSuppressionsPath = _vmrInfo.VmrPath / VmrInfo.SourcesDir / repoPath / location;

            if (!_fileSystem.FileExists(repoCredScanSuppressionsPath)) continue;

            var repoCredScanSuppressionsFile = JsonSerializer.Deserialize<CredScanSuppressionFile>(await _fileSystem.ReadAllTextAsync(repoCredScanSuppressionsPath), _jsonOptions);

            if (repoCredScanSuppressionsFile != null && repoCredScanSuppressionsFile.Suppressions != null)
            {
                foreach (var suppression in repoCredScanSuppressionsFile.Suppressions)
                {
                    if (suppression.File != null)
                    {
                        for (int i = 0; i < suppression.File.Count; i++)
                        {
                            suppression.File[i] = FixCredScanSuppressionsRule(repoPath, suppression.File[i]);
                        }
                    }
                }

                vmrCredScanSuppressionsFile.Suppressions.AddRange(repoCredScanSuppressionsFile.Suppressions);
            }
        }
    }

    /// <summary>
    /// Fixes a CredScanSuppressions.json file rule by prefixing the path with the VMR location and replacing backslash.
    /// </summary>
    private static string FixCredScanSuppressionsRule(string repoPath, string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return file;
        }

        if (file.Contains('\\'))
        {
            file = file.Replace('\\', '/');
        }

        return $"/{VmrInfo.SourcesDir}/{repoPath}{(file.StartsWith('/') ? string.Empty : '/')}{file}";
    }
}

class CredScanSuppressionFile
{
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "Credential Scanner";
    [JsonPropertyName("suppressions")]
    public List<CredScanSuppression> Suppressions { get; set; } = [];
}

class CredScanSuppression
{
    [JsonPropertyName("_justification")]
    public string Justification { get; set; } = "";
    [JsonPropertyName("placeholder")]
    [JsonConverter(typeof(SingleStringOrArrayConverter))]
    public List<string>? Placeholder { get; set; }
    [JsonPropertyName("file")]
    [JsonConverter(typeof(SingleStringOrArrayConverter))]
    public List<string>? File { get; set; }
}

class SingleStringOrArrayConverter : JsonConverter<List<string>>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;
                    var arrayItem = JsonSerializer.Deserialize<string>(ref reader, options);
                    if (arrayItem != null)
                    {
                        list.Add(arrayItem);
                    }
                }
                return list;
            default:
                var item = JsonSerializer.Deserialize<string>(ref reader, options);
                return item != null ? [item] : null;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> objectToWrite, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, objectToWrite, objectToWrite.GetType(), options);
    }
}
