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
    private string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
    private LocalPath _productRepoPath = null!;
    private LocalPath _productRepoFilePath = null!;
    private LocalPath _dependencyRepoFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepoPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoFilePath = _productRepoPath / _productRepoFileName;
        _dependencyRepoFilePath = VmrPath / VmrInfo.SourcesDir / Constants.DependencyRepoName / _dependencyFileName;
    }

    [Test]
    public async Task FileChangesAreSyncedTest()
    {
        await EnsureTestRepoIsInitialized();

        File.WriteAllText(ProductRepoPath / _productRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a file in the repo");

        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoFilePath, "Test changes in repo file");
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(VmrPath);
    }

    [Test]
    public async Task FileIsIncludedTest()
    {
        var excludedDir = ProductRepoPath / "excluded";
        var excludedFileName = "excluded.txt";
        var excludedFile = excludedDir / excludedFileName;

        Directory.CreateDirectory(excludedDir);
        File.WriteAllText(excludedFile, "File to be excluded");
        await GitOperations.CommitAll(ProductRepoPath, "Create an excluded file");

        await EnsureTestRepoIsInitialized();

        File.Move(excludedFile, ProductRepoPath / excludedFileName);
        await GitOperations.CommitAll(ProductRepoPath, "Move a file from excluded to included folder");

        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath,
            _productRepoPath / excludedFileName,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoPath / excludedFileName, "File to be excluded");
        await GitOperations.CheckAllIsCommited(VmrPath);
    }

    [Test]
    public async Task SubmodulesAreInlinedProperlyTest()
    {
        var submodulesDir = "externals";
        var submodulePathInVmr = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / submodulesDir / Constants.SecondRepoName;
        var submoduleFilePath = submodulePathInVmr / Constants.GetRepoFileName(Constants.SecondRepoName);
        var additionalFileName = "additional-file.txt";
        var additionalSubmoduleFilePath = submodulePathInVmr / additionalFileName;
        var submoduleName = "submodule1";

        await EnsureTestRepoIsInitialized();

        var submoduleRelativePath = new NativePath("externals") / Constants.SecondRepoName;
        await GitOperations.InitializeSubmodule(ProductRepoPath, submoduleName, SecondRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(ProductRepoPath, "Add submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath,
            submoduleFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoFilePath, _productRepoFileName);
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        CheckFileContents(submoduleFilePath, "File in product-repo2");
        await GitOperations.CheckAllIsCommited(VmrPath);

        // Add a file in the submodule

        File.WriteAllText(SecondRepoPath / additionalFileName, "New external repo file");
        await GitOperations.CommitAll(SecondRepoPath, "Adding new file in the submodule");
        await GitOperations.PullMain(ProductRepoPath / submoduleRelativePath);
        
        await GitOperations.CommitAll(ProductRepoPath, "Checkout submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        expectedFiles.Add(additionalSubmoduleFilePath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(VmrPath);

        // Remove submodule

        await GitOperations.RemoveSubmodule(ProductRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(ProductRepoPath, "Remove the submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        expectedFiles.Remove(submoduleFilePath);
        expectedFiles.Remove(additionalSubmoduleFilePath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommited(VmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<string>>
        {
            { Constants.ProductRepoName,  new List<string> {Constants.DependencyRepoName} }
        };

        await CopyRepoAndCreateVersionDetails(
            CurrentTestDirectory,
            Constants.ProductRepoName,
            dependenciesMap);

        CopyDirectory(VmrTestsOneTimeSetUp.CommonExternalRepoPath, SecondRepoPath);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.DependencyRepoName,
                    DefaultRemote = DependencyRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
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
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoFilePath, _productRepoFileName);
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        await GitOperations.CheckAllIsCommited(VmrPath);
    }
}

