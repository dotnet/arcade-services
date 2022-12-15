// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrSyncRepoChangesTest :  VmrTestsBase
{
    private readonly string _testFileName = "test-repo-file.txt";

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependency-file.txt";

        await EnsureTestRepoIsInitialized();

        File.WriteAllText(_privateRepoPath / "test-repo-file.txt", "Test changes in repo file");
        await GitOperations.CommitAll(_privateRepoPath, "Changing a file in the repo");

        await UpdateRepoToLastCommit("test-repo", _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(testRepoFilePath, "Test changes in repo file");
        CheckFileContents(dependencyFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        Directory.CreateDirectory(_privateRepoPath / "excluded");
        File.WriteAllText(_privateRepoPath / "excluded" / "excluded.txt", "File to be excluded");
        await GitOperations.CommitAll(_privateRepoPath, "Create an excluded file");

        await EnsureTestRepoIsInitialized();

        File.Move(_privateRepoPath / "excluded" / "excluded.txt", _privateRepoPath / "excluded.txt");
        await GitOperations.CommitAll(_privateRepoPath, "Move a file from excluded to included folder");

        await UpdateRepoToLastCommit("test-repo", _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / "src" / "test-repo" / "test-repo-file.txt",
            _vmrPath / "src" / "dependency" / "dependency-file.txt",
            _vmrPath / "src" / "test-repo" / "excluded.txt",
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(_vmrPath / "src" / "test-repo" / "excluded.txt", "File to be excluded");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task SubmodulesAreInlinedProperlyTest()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependency-file.txt";
        var submoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "external-repo-file.txt";
        var additionalSubmoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "additional-file.txt";

        await EnsureTestRepoIsInitialized();

        var submoduleRelativePath = new NativePath("externals") / "external-repo";
        await GitOperations.InitializeSubmodule(_privateRepoPath, "submodule1", _externalRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(_privateRepoPath, "Add submodule");
        await UpdateRepoToLastCommit("test-repo", _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath,
            submoduleFilePath,
            _vmrPath / "src" / "test-repo" / ".gitmodules",
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, "test-repo-file.txt");
        CheckFileContents(dependencyFilePath, "File in dependency");
        CheckFileContents(submoduleFilePath, "File in external-repo");
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // Add a file in the submodule

        File.WriteAllText(_externalRepoPath / "additional-file.txt", "New external repo file");
        await GitOperations.CommitAll(_externalRepoPath, "Adding new file in the submodule");
        await GitOperations.PullMain(_privateRepoPath / submoduleRelativePath);
        
        await GitOperations.CommitAll(_privateRepoPath, "Checkout submodule");
        await UpdateRepoToLastCommit("test-repo", _privateRepoPath);

        expectedFiles.Add(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // Remove submodule

        await GitOperations.RemoveSubmodule(_privateRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(_privateRepoPath, "Remove the submodule");
        await UpdateRepoToLastCommit("test-repo", _privateRepoPath);

        expectedFiles.Remove(submoduleFilePath);
        expectedFiles.Remove(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<Dependency>>
        {
            {Constants.TestRepoName,  new List<Dependency> {new Dependency("dependency", _dependencyRepoPath) } }
        };

        await CopyRepoAndCreateVersionDetails(
            _currentTestDirectory,
            Constants.TestRepoName,
            dependenciesMap);

        CopyDirectory(VmrTestsOneTimeSetUp.CommonExternalRepoPath, _externalRepoPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var mappings = new List<SourceMapping>
        {
            new SourceMapping("dependency", _dependencyRepoPath.Path.Replace("\\", "\\\\")),
            new SourceMapping("test-repo", _privateRepoPath.Path.Replace("\\", "\\\\"),
            new List<string> { "externals/external-repo/**/*.exe", "excluded/*" })
        };

        var sm = GenerateSourceMappings(mappings);

        File.WriteAllText(_vmrPath / "src" / "source-mappings.json", sm);
        await GitOperations.CommitAll(_vmrPath, "Add source mappings");
    }

    private async Task EnsureTestRepoIsInitialized()
    {
        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";
        var dependencyFilePath = _vmrPath / "src" / "dependency" / "dependency-file.txt";

        await InitializeRepoAtLastCommit("test-repo", _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            dependencyFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "dependency" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, _testFileName);
        CheckFileContents(dependencyFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }
}

