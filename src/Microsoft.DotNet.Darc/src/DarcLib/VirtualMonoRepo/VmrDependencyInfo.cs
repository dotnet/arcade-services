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

public interface IVmrDependencyInfo
{
    string VmrPath { get; }

    string SourcesPath { get; }
    
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    string GetRepoSourcesPath(SourceMapping mapping) => Path.Combine(SourcesPath, mapping.Name);

    void UpdateDependencyVersion(SourceMapping mapping, string sha, string? version);

    (string? Sha, string? Version)? GetDependencyVersion(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the AllRepoVersions.props file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyInfo : IVmrDependencyInfo
{
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string VmrSourcesDir = "src";
    public const string GitInfoSourcesDir = "git-info";

    // TODO: https://github.com/dotnet/source-build/issues/2250
    private const string DefaultVersion = "7.0.100";

    private readonly Lazy<AllVersionsPropsFile> _repoVersions;
    private readonly string _allVersionsFilePath;

    public string VmrPath { get; }

    public string SourcesPath { get; }

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrDependencyInfo(
        IVmrManagerConfiguration configuration,
        IReadOnlyCollection<SourceMapping> mappings)
    {
        VmrPath = configuration.VmrPath;
        SourcesPath = Path.Combine(configuration.VmrPath, VmrSourcesDir);
        Mappings = mappings;

        _allVersionsFilePath = Path.Combine(VmrPath, GitInfoSourcesDir, AllVersionsPropsFile.FileName);
        _repoVersions = new Lazy<AllVersionsPropsFile>(
            () => AllVersionsPropsFile.DeserializeFromXml(_allVersionsFilePath),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public (string? Sha, string? Version)? GetDependencyVersion(SourceMapping mapping)
        => _repoVersions.Value.GetVersion(mapping.Name);

    public void UpdateDependencyVersion(SourceMapping mapping, string sha, string? version)
    {
        // TODO: https://github.com/dotnet/source-build/issues/2250
        version ??= DefaultVersion;

        _repoVersions.Value.UpdateVersion(mapping.Name, sha, version);
        _repoVersions.Value.SerializeToXml(_allVersionsFilePath);

        var (buildId, releaseLabel) = VersionFiles.DeriveBuildInfo(mapping.Name, version);
        
        var gitInfo = new GitInfoFile
        {
            GitCommitHash = sha,
            OfficialBuildId = buildId,
            PreReleaseVersionLabel = releaseLabel,
            IsStable = string.IsNullOrWhiteSpace(releaseLabel),
            OutputPackageVersion = version,
        };

        gitInfo.SerializeToXml(GetGitInfoFilePath(mapping));
    }

    private string GetGitInfoFilePath(SourceMapping mapping) => Path.Combine(VmrPath, GitInfoSourcesDir, $"{mapping.Name}.props");
}
