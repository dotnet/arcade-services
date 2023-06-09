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

public class VmrRecursiveSyncTests : VmrTestsBase
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
            new[] 
            { 
                Constants.InstallerRepoName, 
                Constants.ProductRepoName, 
                Constants.SecondRepoName, 
                Constants.DependencyRepoName 
            },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // create new version of dependency repo

        File.WriteAllText(
            DependencyRepoPath / Constants.GetRepoFileName(Constants.DependencyRepoName), 
            "New version of the file");
        await GitOperations.CommitAll(DependencyRepoPath, "change the file in dependency repo");

        // the second repo depends on the new version, first repo depends on the old one

        var sha = await GitOperations.GetRepoLastCommit(DependencyRepoPath);
        var dependencyString = string.Format(
            Constants.DependencyTemplate, 
            new[] { Constants.DependencyRepoName, DependencyRepoPath, sha });

        var versionDetails = string.Format(Constants.VersionDetailsTemplate, dependencyString);
        File.WriteAllText(SecondRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        File.WriteAllText(
            SecondRepoPath / Constants.GetRepoFileName(Constants.SecondRepoName), 
            "New version of product-repo2 file");
        await GitOperations.CommitAll(SecondRepoPath, "update version details");

        // update installers Version.Details

        var newSecondRepoSha = await GitOperations.GetRepoLastCommit(SecondRepoPath);
        var productRepoSha = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        var productRepoDependency = string.Format(
            Constants.DependencyTemplate, 
            new[] { Constants.ProductRepoName, ProductRepoPath, productRepoSha });

        var secondRepoDependency = string.Format(
            Constants.DependencyTemplate, 
            new[] { Constants.SecondRepoName, SecondRepoPath, newSecondRepoSha });

        versionDetails = string.Format(
            Constants.VersionDetailsTemplate, 
            productRepoDependency + Environment.NewLine + secondRepoDependency);

        File.WriteAllText(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        File.WriteAllText(
            InstallerRepoPath / Constants.GetRepoFileName(Constants.InstallerRepoName), 
            "New version of installer file");
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

        CheckFileContents(installerFilePath, "New version of installer file");
        CheckFileContents(secondRepoFilePath, "New version of product-repo2 file");
        CompareFileContents(firstRepoFilePath, Constants.GetRepoFileName(Constants.ProductRepoName));

        // the new version of dependency shouldn't be pulled in the vmr

        CheckFileContents(dependencyFilePath, "File in dependency");
    }

    protected override async Task CopyReposForCurrentTest()
    {
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
            { Constants.ProductRepoName, new List<string> {Constants.DependencyRepoName} },
            { Constants.SecondRepoName, new List<string> {Constants.DependencyRepoName }},
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
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
