// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrSyncRepoChangesTest :  VmrTestsBase
{
    private readonly string _dependencyFileName = "dependency-file.txt";
    private LocalPath _productRepoPath = null!;
    private LocalPath _productRepoFilePath = null!;
    private LocalPath _dependencyRepoFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepoPath = _vmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoFilePath = _productRepoPath / Constants.ProductRepoFileName;
        _dependencyRepoFilePath = _vmrPath / VmrInfo.SourcesDir / Constants.DependencyRepoName / _dependencyFileName;
    }

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        await EnsureTestRepoIsInitialized();

        File.WriteAllText(_privateRepoPath / Constants.ProductRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(_privateRepoPath, "Changing a file in the repo");

        await UpdateRepoToLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(_productRepoFilePath, "Test changes in repo file");
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        var excludedDir = _privateRepoPath / "excluded";
        var excludedFileName = "excluded.txt";
        var excludedFile = excludedDir / excludedFileName;


        Directory.CreateDirectory(excludedDir);
        File.WriteAllText(excludedFile, "File to be excluded");
        await GitOperations.CommitAll(_privateRepoPath, "Create an excluded file");

        await EnsureTestRepoIsInitialized();

        File.Move(excludedFile, _privateRepoPath / excludedFileName);
        await GitOperations.CommitAll(_privateRepoPath, "Move a file from excluded to included folder");

        await UpdateRepoToLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath,
            _productRepoPath / excludedFileName,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(_productRepoPath / excludedFileName, "File to be excluded");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    [Test]
    public async Task SubmodulesAreInlinedProperlyTest()
    {
        var submoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / "external-repo-file.txt";
        var additionalFileName = "additional-file.txt";
        var additionalSubmoduleFilePath = _vmrPath / "src" / "test-repo" / "externals" / "external-repo" / additionalFileName;
        var submoduleName = "submodule1";

        await EnsureTestRepoIsInitialized();

        var submoduleRelativePath = new NativePath("externals") / Constants.SubmoduleRepoName;
        await GitOperations.InitializeSubmodule(_privateRepoPath, submoduleName, _externalRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(_privateRepoPath, "Add submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath,
            submoduleFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(_productRepoFilePath, Constants.ProductRepoFileName);
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        CheckFileContents(submoduleFilePath, "File in external-repo");
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // Add a file in the submodule

        File.WriteAllText(_externalRepoPath / additionalFileName, "New external repo file");
        await GitOperations.CommitAll(_externalRepoPath, "Adding new file in the submodule");
        await GitOperations.PullMain(_privateRepoPath / submoduleRelativePath);
        
        await GitOperations.CommitAll(_privateRepoPath, "Checkout submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, _privateRepoPath);

        expectedFiles.Add(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // Remove submodule

        await GitOperations.RemoveSubmodule(_privateRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(_privateRepoPath, "Remove the submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, _privateRepoPath);

        expectedFiles.Remove(submoduleFilePath);
        expectedFiles.Remove(additionalSubmoduleFilePath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<string>>
        {
            {Constants.ProductRepoName,  new List<string> {Constants.DependencyRepoName} }
        };

        await CopyRepoAndCreateVersionDetails(
            _currentTestDirectory,
            Constants.ProductRepoName,
            dependenciesMap);

        CopyDirectory(VmrTestsOneTimeSetUp.CommonExternalRepoPath, _externalRepoPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = _dependencyRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = _privateRepoPath
                }
            }
        };

        sourceMappings.Defaults.Exclude = new[] 
        {
            "externals/external-repo/**/*.exe", 
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }

    private async Task EnsureTestRepoIsInitialized()
    {
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(_productRepoFilePath, Constants.ProductRepoFileName);
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(_vmrPath);
    }
}

