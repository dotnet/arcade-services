// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrRecursiveSyncTests : VmrTestsBase
{
    private LocalPath _firstInstallerDependencyPath = null!;
    private LocalPath _secondInstallerDependencyPath = null!;
    private string _firstInstallerDependencyName = null!;
    private string _secondInstallerDependencyName = null!;

    [Test]
    public async Task RecursiveUpdatePreservesDependencyVersionTest()
    {
        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName),
            _vmrPath / VmrInfo.SourcesDir / _firstInstallerDependencyName / Constants.GetRepoFileName(_firstInstallerDependencyName),
            _vmrPath / VmrInfo.SourcesDir / _secondInstallerDependencyName / Constants.GetRepoFileName(_secondInstallerDependencyName),
            _vmrPath / VmrInfo.SourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName),
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.InstallerRepoName, _firstInstallerDependencyName, _secondInstallerDependencyName, Constants.DependencyRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(_vmrPath, expectedFiles);

        // create new version of dependency repo

        File.WriteAllText(_dependencyRepoPath / Constants.DependencyRepoFileName, "New version of the file");
        await GitOperations.CommitAll(_dependencyRepoPath, "change the file in dependency repo");

        // the second repo depends on the new version, first repo depends on the old one

        var sha = await GitOperations.GetRepoLastCommit(_dependencyRepoPath);
        var dependencyString = string.Format(Constants.DependencyTemplate, new[] { Constants.DependencyRepoName, _dependencyRepoPath, sha });
        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependencyString);
        File.WriteAllText(_secondInstallerDependencyPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(_secondInstallerDependencyPath, "update version details");

        // update installers Version.Details

        var newRuntimeSha = await GitOperations.GetRepoLastCommit(_secondInstallerDependencyPath);
        var aspnetSha = await GitOperations.GetRepoLastCommit(_firstInstallerDependencyPath);
        var aspnetDependency = string.Format(Constants.DependencyTemplate, new[] { _firstInstallerDependencyName, _firstInstallerDependencyPath, aspnetSha });
        var runtimeDependency = string.Format(Constants.DependencyTemplate, new[] { _secondInstallerDependencyName, _secondInstallerDependencyPath, newRuntimeSha });
        versionDetails = string.Format(Constants.VersionDetailsTemplate, aspnetDependency + Environment.NewLine + runtimeDependency);
        File.WriteAllText(_installerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(_installerRepoPath, "update version details");

        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        // the new version of dependency shouldn't be pulled in the vmr

        CheckFileContents(_vmrPath / VmrInfo.SourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName), "File in dependency");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        _firstInstallerDependencyPath = _privateRepoPath;
        _secondInstallerDependencyPath = _externalRepoPath;
        _firstInstallerDependencyName = Constants.ProductRepoName;
        _secondInstallerDependencyName = Constants.SubmoduleRepoName;

        var dependenciesMap = new Dictionary<string, List<string>>
        {
            {
                Constants.InstallerRepoName,
                new List<string>
                {
                    Constants.ProductRepoName, 
                    Constants.SubmoduleRepoName
                }
            },
            {Constants.ProductRepoName, new List<string> {Constants.DependencyRepoName} },
            {Constants.SubmoduleRepoName, new List<string> {Constants.DependencyRepoName }},
        };

        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.InstallerRepoName, dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = _installerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = _firstInstallerDependencyName,
                    DefaultRemote = _firstInstallerDependencyPath
                },
                new SourceMappingSetting
                {
                    Name = _secondInstallerDependencyName,
                    DefaultRemote = _secondInstallerDependencyPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = _dependencyRepoPath
                }
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
