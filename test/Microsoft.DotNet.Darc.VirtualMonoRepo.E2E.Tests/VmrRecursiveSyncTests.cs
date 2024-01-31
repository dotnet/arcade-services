// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

internal class VmrRecursiveSyncTests : VmrTestsBase
{
    [Test]
    public async Task RecursiveUpdatePreservesDependencyVersionTest()
    {
        var vmrSourcesDir = VmrPath / VmrInfo.SourcesDir;
        var installerFilePath = vmrSourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var firstRepoFilePath = vmrSourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName);
        var secondRepoFilePath = vmrSourcesDir / Constants.SecondRepoName / Constants.GetRepoFileName(Constants.SecondRepoName);
        var dependencyFilePath = vmrSourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName);

        /* 
         *  The dependency tree looks like:
         *
         *  └── installer           1.0.0 *
         *      ├── product-repo1   1.0.0 *
         *      │   └── dependency  1.0.0 *
         *      └── product-repo2   1.0.0 *
         *          └── dependency  1.0.0
         *
         *  (* marks which version will be in the VMR)
         */

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        var expectedFilesFromRepos = new List<NativePath>
        {
            installerFilePath,
            firstRepoFilePath,
            secondRepoFilePath,
            dependencyFilePath,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [ 
                Constants.InstallerRepoName, 
                Constants.ProductRepoName, 
                Constants.SecondRepoName, 
                Constants.DependencyRepoName 
            ],
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Create new version of dependency repo

        File.WriteAllText(
            DependencyRepoPath / Constants.GetRepoFileName(Constants.DependencyRepoName), 
            "New version of the file");
        await GitOperations.CommitAll(DependencyRepoPath, "change the file in dependency repo");

        // The second repo depends on the new version, first repo depends on the old one

        var sha = await GitOperations.GetRepoLastCommit(DependencyRepoPath);
        var dependencyString = string.Format(
            Constants.DependencyTemplate, 
            Constants.DependencyRepoName, DependencyRepoPath, sha);

        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependencyString);
        File.WriteAllText(SecondRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        File.WriteAllText(
            SecondRepoPath / Constants.GetRepoFileName(Constants.SecondRepoName), 
            "New version of product-repo2 file");
        await GitOperations.CommitAll(SecondRepoPath, "update version details");

        // Update installers Version.Details

        var newSecondRepoSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);
        var productRepoSha = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        var productRepoDependency = string.Format(
            Constants.DependencyTemplate, 
            Constants.ProductRepoName, ProductRepoPath, productRepoSha);

        var secondRepoDependency = string.Format(
            Constants.DependencyTemplate, 
            Constants.SecondRepoName, SecondRepoPath, newSecondRepoSha);

        versionDetails = string.Format(
            Constants.VersionDetailsTemplate, 
            productRepoDependency + Environment.NewLine + secondRepoDependency);

        File.WriteAllText(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        File.WriteAllText(
            InstallerRepoPath / Constants.GetRepoFileName(Constants.InstallerRepoName), 
            "New version of installer file");
        await GitOperations.CommitAll(InstallerRepoPath, "update version details");

        /* 
         *  The dependency tree should look like this:
         *
         *    └── installer           1.0.1 *
         *        ├── product-repo1   1.0.0 *
         *        │   └── dependency  1.0.0 *
         *        └── product-repo2   1.0.1 *
         *            └── dependency  1.0.1     < This bump is ignored because product-repo1 depends on 1.0.0
        */

        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckFileContents(installerFilePath, "New version of installer file");
        CheckFileContents(secondRepoFilePath, "New version of product-repo2 file");
        CompareFileContents(firstRepoFilePath, Constants.GetRepoFileName(Constants.ProductRepoName));

        // The new version of dependency shouldn't be pulled in the VMR

        CheckFileContents(dependencyFilePath, "File in dependency");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<string>>
        {
            {
                Constants.InstallerRepoName,
                [
                    Constants.ProductRepoName, 
                    Constants.SecondRepoName
                ]
            },
            { Constants.ProductRepoName, [Constants.DependencyRepoName] },
            { Constants.SecondRepoName, [Constants.DependencyRepoName] },
        };

        await CopyRepoAndCreateVersionFiles(Constants.InstallerRepoName, dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings =
            [
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = InstallerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.SecondRepoName,
                    DefaultRemote = SecondRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = DependencyRepoPath
                }
            ],
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
