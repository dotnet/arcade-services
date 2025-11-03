// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class SourceMappingManager : ISourceMappingManager
{
    private readonly IVmrInfo _vmrInfo;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly IFileSystem _fileSystem;
    private readonly ILocalGitClient _localGitClient;
    private readonly ILogger<SourceMappingManager> _logger;

    public SourceMappingManager(
        IVmrInfo vmrInfo,
        IVmrDependencyTracker dependencyTracker,
        IFileSystem fileSystem,
        ILocalGitClient localGitClient,
        ILogger<SourceMappingManager> logger)
    {
        _vmrInfo = vmrInfo;
        _dependencyTracker = dependencyTracker;
        _fileSystem = fileSystem;
        _localGitClient = localGitClient;
        _logger = logger;
    }

    public async Task<bool> EnsureSourceMappingExistsAsync(
        string repoName,
        string? defaultRemote,
        LocalPath sourceMappingsPath,
        CancellationToken cancellationToken)
    {
        // Refresh metadata to load existing mappings
        await _dependencyTracker.RefreshMetadataAsync(sourceMappingsPath);

        // Check if mapping already exists
        if (_dependencyTracker.TryGetMapping(repoName, out var existingMapping))
        {
            _logger.LogInformation("Source mapping for '{repoName}' already exists", repoName);
            return false;
        }

        // Read the existing source-mappings.json file
        var json = await _fileSystem.ReadAllTextAsync(sourceMappingsPath);
        
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
        };

        var sourceMappingFile = JsonSerializer.Deserialize<SourceMappingFile>(json, options)
            ?? throw new Exception($"Failed to deserialize {VmrInfo.SourceMappingsFileName}");

        // Determine the default remote URL
        // If not provided, use GitHub dotnet org
        defaultRemote ??= $"https://github.com/dotnet/{repoName}";
        
        // Add the new mapping
        var newMapping = new SourceMappingSetting
        {
            Name = repoName,
            DefaultRemote = defaultRemote,
        };

        sourceMappingFile.Mappings.Add(newMapping);

        // Write the updated source-mappings.json file
        var updatedJson = JsonSerializer.Serialize(sourceMappingFile, options);
        _fileSystem.WriteToFile(sourceMappingsPath, updatedJson);

        // Stage the source-mappings.json file
        await _localGitClient.StageAsync(_vmrInfo.VmrPath, new[] { sourceMappingsPath.ToString() }, cancellationToken);
        
        _logger.LogInformation("Added source mapping for '{repoName}' with remote '{defaultRemote}' and staged {file}", 
            repoName, defaultRemote, VmrInfo.SourceMappingsFileName);

        return true;
    }
}
