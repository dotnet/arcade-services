// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyVersion(string Sha, string? PackageVersion);

public interface IVmrDependencyTracker
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    /// <summary>
    /// Loads repository mappings from source-mappings.json
    /// </summary>
    /// <param name="sourceMappingsPath">Leave empty for default (src/source-mappings.json)</param>
    Task InitializeSourceMappings(string? sourceMappingsPath = null);

    void UpdateDependencyVersion(VmrDependencyUpdate update);

    void UpdateSubmodules(List<SubmoduleRecord> submodules);

    bool RemoveRepositoryVersion(string repo);

    VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping);
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
        get => _mappings ?? throw new System.Exception("Source mappings have not been initialized.");
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

    public VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping)
        => _sourceManifest.GetVersion(mapping.Name);

    public async Task InitializeSourceMappings(string? sourceMappingsPath = null)
    {
        sourceMappingsPath ??= _vmrInfo.VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName;
        _mappings = await _sourceMappingParser.ParseMappings(sourceMappingsPath);
    }

    public void UpdateDependencyVersion(VmrDependencyUpdate update)
    {
        _repoVersions.UpdateVersion(update.Mapping.Name, update.TargetRevision, update.TargetVersion);
        _repoVersions.SerializeToXml(_allVersionsFilePath);

        _sourceManifest.UpdateVersion(update.Mapping.Name, update.RemoteUri, update.TargetRevision, update.TargetVersion);
        _fileSystem.WriteToFile(_vmrInfo.GetSourceManifestPath(), _sourceManifest.ToJson());

        // Root repository of an update does not have a package version associated with it
        // For installer, we leave whatever was there (e.g. 8.0.100)
        // For one-off non-recursive updates of repositories, we keep the previous
        string packageVersion = update.TargetVersion
            ?? _sourceManifest.GetVersion(update.Mapping.Name)?.PackageVersion
            ?? "0.0.0";

        var (buildId, releaseLabel) = VersionFiles.DeriveBuildInfo(update.Mapping.Name, packageVersion);
        
        var gitInfo = new GitInfoFile
        {
            GitCommitHash = update.TargetRevision,
            OfficialBuildId = buildId,
            PreReleaseVersionLabel = releaseLabel,
            IsStable = string.IsNullOrWhiteSpace(releaseLabel),
            OutputPackageVersion = packageVersion,
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

        _fileSystem.WriteToFile(_vmrInfo.GetSourceManifestPath(), _sourceManifest.ToJson());
    }

    private string GetGitInfoFilePath(SourceMapping mapping) => GetGitInfoFilePath(mapping.Name);

    private string GetGitInfoFilePath(string mappingName) => _vmrInfo.VmrPath / VmrInfo.GitInfoSourcesDir / $"{mappingName}.props";
}
