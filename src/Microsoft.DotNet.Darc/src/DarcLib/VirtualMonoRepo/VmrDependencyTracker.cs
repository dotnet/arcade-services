// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyVersion(string Sha, string PackageVersion);

public interface IVmrDependencyTracker
{
    string VmrPath { get; }

    string SourcesPath { get; }
    
    IReadOnlyCollection<SourceMapping> Mappings { get; }

    string GetRepoSourcesPath(SourceMapping mapping) => Path.Combine(SourcesPath, mapping.Name);

    void UpdateDependencyVersion(SourceMapping mapping, VmrDependencyVersion version);

    VmrDependencyVersion GetDependencyVersion(SourceMapping mapping);
}

/// <summary>
/// Holds information about versions of individual repositories synchronized in the VMR.
/// Uses the AllRepoVersions.props file as source of truth and propagates changes into the git-info files.
/// </summary>
public class VmrDependencyTracker : IVmrDependencyTracker
{
    public const string SourceMappingsFileName = "source-mappings.json";
    public const string VmrSourcesDir = "src";
    public const string GitInfoSourcesDir = "git-info";

    // TODO: https://github.com/dotnet/source-build/issues/2250
    private const string DefaultVersion = "8.0.100";

    private readonly Lazy<AllVersionsPropsFile> _repoVersions;
    private readonly string _allVersionsFilePath;

    public string VmrPath { get; }

    public string SourcesPath { get; }

    public IReadOnlyCollection<SourceMapping> Mappings { get; }

    public VmrDependencyTracker(
        IVmrManagerConfiguration configuration,
        IReadOnlyCollection<SourceMapping> mappings)
    {
        VmrPath = configuration.VmrPath;
        SourcesPath = Path.Combine(configuration.VmrPath, VmrSourcesDir);
        Mappings = mappings;

        _allVersionsFilePath = Path.Combine(VmrPath, GitInfoSourcesDir, AllVersionsPropsFile.FileName);
        _repoVersions = new Lazy<AllVersionsPropsFile>(LoadAllVersionsFile, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public VmrDependencyVersion GetDependencyVersion(SourceMapping mapping)
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

    private string GetGitInfoFilePath(SourceMapping mapping) => Path.Combine(VmrPath, GitInfoSourcesDir, $"{mapping.Name}.props");
}
