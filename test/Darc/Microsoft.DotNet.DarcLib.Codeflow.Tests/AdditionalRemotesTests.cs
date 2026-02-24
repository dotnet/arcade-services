// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

internal class AdditionalRemotesTests : CodeFlowTestsBase
{
    private NativePath FirstDependencyPath => CurrentTestDirectory / (Constants.DependencyRepoName + "1");
    private NativePath SecondDependencyPath => CurrentTestDirectory / (Constants.DependencyRepoName + "2");

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

        var expectedFilesFromRepos = new List<NativePath>
        {
            dependencyFilePath,
        };

        var expectedFiles = GetExpectedFilesInVmr(VmrPath, [Constants.DependencyRepoName], expectedFilesFromRepos);

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

        await CopyRepoAndCreateVersionFiles(Constants.InstallerRepoName, dependenciesMap);

        // Prepare dependencies at paths 1 and 2
        Directory.Move(DependencyRepoPath, FirstDependencyPath);
        CopyDirectory(FirstDependencyPath, SecondDependencyPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(CodeflowTestsOneTimeSetUp.CommonVmrPath, VmrPath);

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
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = FirstDependencyPath
                }
            ]
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
