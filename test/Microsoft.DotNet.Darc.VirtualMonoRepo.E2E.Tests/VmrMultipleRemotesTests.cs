// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrMultipleRemotesTests : VmrTestsBase
{
    private NativePath FirstDependencyPath => CurrentTestDirectory / (Constants.DependencyRepoName + "1");
    private NativePath SecondDependencyPath => CurrentTestDirectory / (Constants.DependencyRepoName + "2");

    /// <summary>
    /// The dependency tree of repos in this test looks like:
    /// 
    /// installer
    ///   └── dependency
    ///         
    /// We will have two copies of "dependency" in folders dependency1 and dependency2
    /// We will synchronize to the first one and then to the second one
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task SynchronizationBetweenDifferentRemotesTest()
    {
        var vmrSourcesDir = VmrPath / VmrInfo.SourcesDir;
        var installerFilePath = vmrSourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var dependencyFilePath = vmrSourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName);

        var versionDetailsPath = InstallerRepoPath / VersionFiles.VersionDetailsXml;
        var versionDetailsContent = await File.ReadAllTextAsync(versionDetailsPath);
        versionDetailsContent = versionDetailsContent.Replace(
            CurrentTestDirectory / Constants.DependencyRepoName,
            CurrentTestDirectory / Constants.DependencyRepoName + "1");
        await File.WriteAllTextAsync(versionDetailsPath, versionDetailsContent);
        await GitOperations.CommitAll(InstallerRepoPath, "Point VersionDetails.xml to first location");

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            installerFilePath,
            dependencyFilePath,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] 
            { 
                Constants.InstallerRepoName,
                Constants.DependencyRepoName, 
            },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Prepare a new commit in the second dependency repo
        await File.WriteAllTextAsync(
            SecondDependencyPath / Constants.GetRepoFileName(Constants.DependencyRepoName),
            "New content only in the second folder now");
        await GitOperations.CommitAll(SecondDependencyPath, "New commit in the second dependency repo");

        // Point installer's VersionDetails.xml to this new repo
        var oldSha = await GitOperations.GetRepoLastCommit(FirstDependencyPath);
        var newSha = await GitOperations.GetRepoLastCommit(SecondDependencyPath);

        versionDetailsContent = await File.ReadAllTextAsync(versionDetailsPath);
        versionDetailsContent = versionDetailsContent.Replace(
            CurrentTestDirectory / Constants.DependencyRepoName + "1",
            CurrentTestDirectory / Constants.DependencyRepoName + "2");
        versionDetailsContent = versionDetailsContent.Replace(oldSha, newSha);
        await File.WriteAllTextAsync(versionDetailsPath, versionDetailsContent);
        await GitOperations.CommitAll(InstallerRepoPath, "Point VersionDetails.xml to second location");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckFileContents(dependencyFilePath, "New content only in the second folder now");
    }

    /// <summary>
    /// In this test:
    ///   - We will have two copies of a repo in folders dependency1 and dependency2
    ///   - We will create a commit only in the second folder
    ///   - We will synchronize the VMR using --additional-remote to pull the commits from both folders
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task SynchronizationWithAdditionalRemoteTest()
    {
        var vmrSourcesDir = VmrPath / VmrInfo.SourcesDir;
        var dependencyFilePath = vmrSourcesDir / Constants.DependencyRepoName / Constants.GetRepoFileName(Constants.DependencyRepoName);

        await InitializeRepoAtLastCommit(Constants.DependencyRepoName, FirstDependencyPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            dependencyFilePath,
        };

        var expectedFiles = GetExpectedFilesInVmr(VmrPath, new[] { Constants.DependencyRepoName }, expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Prepare a new commit in the second dependency repo
        await File.WriteAllTextAsync(
            SecondDependencyPath / Constants.GetRepoFileName(Constants.DependencyRepoName),
            "New content only in the second folder now");
        await GitOperations.CommitAll(SecondDependencyPath, "New commit in the second dependency repo");

        // Get SHA that is only in the second folder which is not in source-mapping.json
        var newSha = await GitOperations.GetRepoLastCommit(SecondDependencyPath);

        var additionalRemotes = new[]
        {
            new AdditionalRemote(Constants.DependencyRepoName,  SecondDependencyPath)
        };

        await CallDarcUpdate(Constants.DependencyRepoName, newSha, additionalRemotes);

        CheckFileContents(dependencyFilePath, "New content only in the second folder now");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<string>>
        {
            { Constants.InstallerRepoName, new List<string> { Constants.DependencyRepoName } },
        };

        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName, dependenciesMap);

        // Prepare dependencies at paths 1 and 2
        Directory.Move(DependencyRepoPath, FirstDependencyPath);
        CopyDirectory(FirstDependencyPath, SecondDependencyPath);
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
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = FirstDependencyPath
                }
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
