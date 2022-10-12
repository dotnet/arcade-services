// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyVersion(string Sha, string? PackageVersion);

public interface IVmrDependencyTracker
{
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    void UpdateDependencyVersion(SourceMapping mapping, VmrDependencyVersion version);

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

    private readonly Lazy<AllVersionsPropsFile> _repoVersions;
    private readonly string _allVersionsFilePath;
    private readonly IVmrInfo _vmrInfo;

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrDependencyTracker(IVmrInfo vmrInfo, IReadOnlyCollection<SourceMapping> mappings)
    {
        _vmrInfo = vmrInfo;
        _allVersionsFilePath = Path.Combine(vmrInfo.VmrPath, VmrInfo.GitInfoSourcesDir, AllVersionsPropsFile.FileName);
        _repoVersions = new Lazy<AllVersionsPropsFile>(LoadAllVersionsFile, LazyThreadSafetyMode.ExecutionAndPublication);

        Mappings = mappings;
    }

    public VmrDependencyVersion? GetDependencyVersion(SourceMapping mapping)
        => _repoVersions.Value.GetVersion(mapping.Name);

    public void UpdateDependencyVersion(SourceMapping mapping, VmrDependencyVersion version)
    {
        // TODO: https://github.com/dotnet/source-build/issues/2250
        if (version.PackageVersion is null)
        {
            version = version with { PackageVersion = DefaultVersion };
        }

        _repoVersions.Value.UpdateVersion(mapping.Name, version.Sha, version.PackageVersion);
        _repoVersions.Value.SerializeToXml(_allVersionsFilePath);

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

    private AllVersionsPropsFile LoadAllVersionsFile()
    {
        if (!File.Exists(_allVersionsFilePath))
        {
            return new AllVersionsPropsFile(new());
        }

        return AllVersionsPropsFile.DeserializeFromXml(_allVersionsFilePath);
    }

    private string GetGitInfoFilePath(SourceMapping mapping) => Path.Combine(_vmrInfo.VmrPath, VmrInfo.GitInfoSourcesDir, $"{mapping.Name}.props");
}
