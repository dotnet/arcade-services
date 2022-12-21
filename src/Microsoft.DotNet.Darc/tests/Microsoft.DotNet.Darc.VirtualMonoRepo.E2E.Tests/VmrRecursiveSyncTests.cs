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
        var installerFilePath = VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var firstRepoFilePath = VmrPath / VmrInfo.SourcesDir / _firstInstallerDependencyName / Constants.GetRepoFileName(_firstInstallerDependencyName);
        var secondRepoFilePath = VmrPath / VmrInfo.SourcesDir / _secondInstallerDependencyName / Constants.GetRepoFileName(_secondInstallerDependencyName);
        var dependencyFilePath = VmrPath / VmrInfo.SourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName);

        /* 
         *  the dependency tree looks like:
         *  
         *  └── installer           1.0.0 *
         *      ├── test-repo       1.0.0 *
         *      │   └── dependency  1.0.0 *
         *      └── external-repo   1.0.0 *
         *          └── dependency  1.0.0
         *          
         *  (* marks which version is in the VMR)    
         */

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            installerFilePath,
            firstRepoFilePath,
            secondRepoFilePath,
            dependencyFilePath,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.InstallerRepoName, _firstInstallerDependencyName, _secondInstallerDependencyName, Constants.DependencyRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // create new version of dependency repo

        File.WriteAllText(DependencyRepoPath / Constants.GetRepoFileName(Constants.DependencyRepoName), "New version of the file");
        await GitOperations.CommitAll(DependencyRepoPath, "change the file in dependency repo");

        // the second repo depends on the new version, first repo depends on the old one

        var sha = await GitOperations.GetRepoLastCommit(DependencyRepoPath);
        var dependencyString = string.Format(Constants.DependencyTemplate, new[] { Constants.DependencyRepoName, DependencyRepoPath, sha });
        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependencyString);
        File.WriteAllText(_secondInstallerDependencyPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(_secondInstallerDependencyPath, "update version details");

        // update installers Version.Details

        var newRuntimeSha = await GitOperations.GetRepoLastCommit(_secondInstallerDependencyPath);
        var aspnetSha = await GitOperations.GetRepoLastCommit(_firstInstallerDependencyPath);
        var aspnetDependency = string.Format(Constants.DependencyTemplate, new[] { _firstInstallerDependencyName, _firstInstallerDependencyPath, aspnetSha });
        var runtimeDependency = string.Format(Constants.DependencyTemplate, new[] { _secondInstallerDependencyName, _secondInstallerDependencyPath, newRuntimeSha });
        versionDetails = string.Format(Constants.VersionDetailsTemplate, aspnetDependency + Environment.NewLine + runtimeDependency);
        File.WriteAllText(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(InstallerRepoPath, "update version details");

        /* 
         *  the dependency tree should look like :
         *    
         *    └── installer           1.0.1 *
         *        ├── test-repo       1.0.0 *
         *        │   └── dependency  1.0.0 *
         *        └── external-repo   1.0.1 *
         *            └── dependency  1.0.1
         *  
        */

        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        // the new version of dependency shouldn't be pulled in the vmr




        CheckFileContents(dependencyFilePath, "File in dependency");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        _firstInstallerDependencyPath = ProductRepoPath;
        _secondInstallerDependencyPath = SecondRepoPath;
        _firstInstallerDependencyName = Constants.ProductRepoName;
        _secondInstallerDependencyName = Constants.SecondRepoName;

        var dependenciesMap = new Dictionary<string, List<string>>
        {
            {
                Constants.InstallerRepoName,
                new List<string>
                {
                    Constants.ProductRepoName, 
                    Constants.SecondRepoName
                }
            },
            {Constants.ProductRepoName, new List<string> {Constants.DependencyRepoName} },
            {Constants.SecondRepoName, new List<string> {Constants.DependencyRepoName }},
        };

        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName, dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = InstallerRepoPath
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
                    DefaultRemote = DependencyRepoPath
                }
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
