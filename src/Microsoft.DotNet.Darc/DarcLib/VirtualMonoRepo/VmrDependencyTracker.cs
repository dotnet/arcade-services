// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyVersion(string Sha, string? PackageVersion);

public interface IVmrDependencyTracker
{
    bool TryGetMapping(string name, [NotNullWhen(true)] out SourceMapping? mapping);

    SourceMapping GetMapping(string name);

    IReadOnlyCollection<SourceMapping> Mappings { get; }

    /// <summary>
    /// Refreshes all metadata: source mappings, source manifest, ..
    /// </summary>
    /// <param name="sourceMappingsPath">Leave empty for default (src/source-mappings.json)</param>
    Task RefreshMetadata(string? sourceMappingsPath = null);

    void UpdateDependencyVersion(VmrDependencyUpdate update);

    void UpdateSubmodules(List<SubmoduleRecord> submodules);

    bool RemoveRepositoryVersion(string repo);

    VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the source-manifest.json file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyTracker : IVmrDependencyTracker
{
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;
    private readonly ISourceMappingParser _sourceMappingParser;
    private IReadOnlyCollection<SourceMapping>? _mappings;

    public IReadOnlyCollection<SourceMapping> Mappings
    {
        get => _mappings ?? throw new Exception("Source mappings have not been initialized.");
    }
            
    public VmrDependencyTracker(
        IVmrInfo vmrInfo,
        IFileSystem fileSystem,
        ISourceMappingParser sourceMappingParser,
        ISourceManifest sourceManifest)
    {
        _vmrInfo = vmrInfo;
        _sourceManifest = sourceManifest;
        _fileSystem = fileSystem;
        _sourceMappingParser = sourceMappingParser;
        _mappings = null;
    }

    public bool TryGetMapping(string name, [NotNullWhen(true)] out SourceMapping? mapping)
    {
        mapping = Mappings.FirstOrDefault(m => m.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        return mapping != null;
    }

    public SourceMapping GetMapping(string name)
        => TryGetMapping(name, out var mapping)
            ? mapping
            : throw new Exception($"No mapping named {name} found");

    public VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping)
        => _sourceManifest.GetVersion(mapping.Name);

    private async Task InitializeSourceMappings(string? sourceMappingsPath = null)
    {
        sourceMappingsPath ??= _vmrInfo.VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;
        _mappings = await _sourceMappingParser.ParseMappings(sourceMappingsPath);
    }

    public async Task RefreshMetadata(string? sourceMappingsPath = null)
    {
        await InitializeSourceMappings(sourceMappingsPath);
        _sourceManifest.Refresh(_vmrInfo.SourceManifestPath);
    }

    public void UpdateDependencyVersion(VmrDependencyUpdate update)
    {
        _sourceManifest.UpdateVersion(
            update.Mapping.Name,
            update.RemoteUri,
            update.TargetRevision,
            update.TargetVersion,
            update.BarId);
        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, _sourceManifest.ToJson());

        // Root repository of an update does not have a package version associated with it
        // For installer, we leave whatever was there (e.g. 8.0.100)
        // For one-off non-recursive updates of repositories, we keep the previous
        string packageVersion = update.TargetVersion
            ?? _sourceManifest.GetVersion(update.Mapping.Name)?.PackageVersion
            ?? "0.0.0";

        // If we didn't find a Bar build for the update, calculate it the old way
        string? officialBuildId = update.OfficialBuildId;
        if (string.IsNullOrEmpty(officialBuildId))
        {
            var (calculatedOfficialBuildId, _) = VersionFiles.DeriveBuildInfo(update.Mapping.Name, packageVersion);
            officialBuildId = calculatedOfficialBuildId;
        }

        var gitInfo = new GitInfoFile
        {
            GitCommitHash = update.TargetRevision,
            OfficialBuildId = officialBuildId,
            OutputPackageVersion = packageVersion,
        };

        gitInfo.SerializeToXml(GetGitInfoFilePath(update.Mapping));
    }

    public bool RemoveRepositoryVersion(string repo)
    {
        var hasChanges = false;
        
        var gitInfoFilePath = GetGitInfoFilePath(repo);
        if (_fileSystem.FileExists(gitInfoFilePath))
        {
            _fileSystem.DeleteFile(gitInfoFilePath);
            hasChanges = true;
        }

        return hasChanges;
    }

    public void UpdateSubmodules(List<SubmoduleRecord> submodules)
    {
        foreach (var submodule in submodules)
        {
            if (submodule.CommitSha == Constants.EmptyGitObject)
            {
                _sourceManifest.RemoveSubmodule(submodule);
            }
            else
            {
                _sourceManifest.UpdateSubmodule(submodule);
            }
        }

        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, _sourceManifest.ToJson());
    }

    private string GetGitInfoFilePath(SourceMapping mapping) => GetGitInfoFilePath(mapping.Name);

    private string GetGitInfoFilePath(string mappingName) => _vmrInfo.VmrPath / VmrInfo.GitInfoSourcesDir / $"{mappingName}.props";
}
