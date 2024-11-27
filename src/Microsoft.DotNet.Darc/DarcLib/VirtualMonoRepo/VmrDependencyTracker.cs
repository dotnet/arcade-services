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
    /// Loads repository mappings from source-mappings.json
    /// </summary>
    /// <param name="sourceMappingsPath">Leave empty for default (src/source-mappings.json)</param>
    Task InitializeSourceMappings(string? sourceMappingsPath = null);

    void UpdateDependencyVersion(VmrDependencyUpdate update);

    void UpdateSubmodules(List<SubmoduleRecord> submodules);

    bool RemoveRepositoryVersion(string repo);

    string? GetDependencyCommit(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the AllRepoVersions.props file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyTracker : IVmrDependencyTracker
{
    private readonly AllVersionsPropsFile _repoVersions;
    private readonly ISourceManifest _sourceManifest;
    private readonly LocalPath _allVersionsFilePath;
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
        _allVersionsFilePath = vmrInfo.VmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName;
        _sourceManifest = sourceManifest;
        _repoVersions = new AllVersionsPropsFile(sourceManifest.Repositories);
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

    public string? GetDependencyCommit(SourceMapping mapping)
        => _sourceManifest.GetVersion(mapping.Name);

    public async Task InitializeSourceMappings(string? sourceMappingsPath = null)
    {
        sourceMappingsPath ??= _vmrInfo.VmrPath / VmrInfo.DefaultRelativeSourceMappingsPath;
        _mappings = await _sourceMappingParser.ParseMappings(sourceMappingsPath);
    }

    public void UpdateDependencyVersion(VmrDependencyUpdate update)
    {
        _repoVersions.UpdateVersion(update.Mapping.Name, update.Commit);
        _repoVersions.SerializeToXml(_allVersionsFilePath);

        _sourceManifest.UpdateVersion(update.Mapping.Name, update.Repository, update.Commit);
        _fileSystem.WriteToFile(_vmrInfo.SourceManifestPath, _sourceManifest.ToJson());
        
        var gitInfo = new GitInfoFile
        {
            GitCommitHash = update.Commit,
            OfficialBuildId = update.build.AzureDevOpsBuildNumber
        };

        gitInfo.SerializeToXml(GetGitInfoFilePath(update.Mapping));
    }

    public bool RemoveRepositoryVersion(string repo)
    {
        var hasChanges = false;

        if (_repoVersions.DeleteVersion(repo))
        {
            _repoVersions.SerializeToXml(_allVersionsFilePath);
            hasChanges = true;
        }
        
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
