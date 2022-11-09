// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyVersion(string Sha, string? PackageVersion);

public interface IVmrDependencyTracker
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    void UpdateDependencyVersion(SourceMapping mapping, VmrDependencyVersion version);

    void UpdateSubmodules(List<SubmoduleRecord> submodules);

    VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the AllRepoVersions.props file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyTracker : IVmrDependencyTracker
{
    // TODO: https://github.com/dotnet/source-build/issues/2250
    private const string DefaultVersion = "8.0.100";

    private readonly AllVersionsPropsFile _repoVersions;
    private readonly ISourceManifest _sourceManifest;
    private readonly string _allVersionsFilePath;
    private readonly IVmrInfo _vmrInfo;
    private readonly IFileSystem _fileSystem;

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrDependencyTracker(
        IVmrInfo vmrInfo,
        IFileSystem fileSystem,
        IReadOnlyCollection<SourceMapping> mappings,
        ISourceManifest sourceManifest)
    {
        _vmrInfo = vmrInfo;
        _allVersionsFilePath = Path.Combine(vmrInfo.VmrPath, VmrInfo.GitInfoSourcesDir, AllVersionsPropsFile.FileName);
        _sourceManifest = sourceManifest;
        _repoVersions = new AllVersionsPropsFile(sourceManifest.Repositories);
        _fileSystem = fileSystem;
        Mappings = mappings;
    }

    public VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping)
        => _sourceManifest.GetVersion(mapping.Name);

    public void UpdateDependencyVersion(SourceMapping mapping, VmrDependencyVersion version)
    {
        // TODO: https://github.com/dotnet/source-build/issues/2250
        if (version.PackageVersion is null)
        {
            version = version with { PackageVersion = DefaultVersion };
        }

        _repoVersions.UpdateVersion(mapping.Name, version.Sha, version.PackageVersion);
        _repoVersions.SerializeToXml(_allVersionsFilePath);

        _sourceManifest.UpdateVersion(mapping.Name, mapping.DefaultRemote, version.Sha, version.PackageVersion);
        _fileSystem.WriteToFile(_vmrInfo.GetSourceManifestPath(), _sourceManifest.ToJson());

        var (buildId, releaseLabel) = VersionFiles.DeriveBuildInfo(mapping.Name, version.PackageVersion);
        
        var gitInfo = new GitInfoFile
        {
            GitCommitHash = version.Sha,
            OfficialBuildId = buildId,
            PreReleaseVersionLabel = releaseLabel,
            IsStable = string.IsNullOrWhiteSpace(releaseLabel),
            OutputPackageVersion = version.PackageVersion,
        };

        gitInfo.SerializeToXml(GetGitInfoFilePath(mapping));
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

    private string GetGitInfoFilePath(SourceMapping mapping) => Path.Combine(_vmrInfo.VmrPath, VmrInfo.GitInfoSourcesDir, $"{mapping.Name}.props");
}
