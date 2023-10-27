// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
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
    private NativePath _productRepoPath = null!;
    private NativePath _productRepoFilePath = null!;
    private NativePath _dependencyRepoFilePath = null!;

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
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }

    [Test]
    public async Task PackageVersionIsUpdatedOnlyTest()
    {
        await EnsureTestRepoIsInitialized();

        // Update version of dependent repo to 8.0.1
        var versionDetails = await File.ReadAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml);
        versionDetails = versionDetails.Replace("8.0.0", "8.0.1");
        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(ProductRepoPath, "Changing SHA");

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
        CheckFileContents(_productRepoPath / VersionFiles.VersionDetailsXml, versionDetails, removeEmptyLines: false);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        var sourceManifest = SourceManifest.FromJson(VmrPath / VmrInfo.SourcesDir / VmrInfo.SourceManifestFileName);

        sourceManifest.GetVersion(Constants.DependencyRepoName)!.PackageVersion.Should().Be("8.0.1");
        (await File.ReadAllTextAsync(VmrPath / VmrInfo.GitInfoSourcesDir / Constants.DependencyRepoName + ".props")).Should().Contain("8.0.1");
        (await File.ReadAllTextAsync(VmrPath / VmrInfo.GitInfoSourcesDir / AllVersionsPropsFile.FileName)).Should().Contain("8.0.1");
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
        await GitOperations.CheckAllIsCommitted(VmrPath);
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
        Directory.CreateDirectory(Path.GetDirectoryName(ProductRepoPath / VmrInfo.CodeownersPath)!);
        await File.WriteAllTextAsync(ProductRepoPath / VmrInfo.CodeownersPath, "# This is a first repo's CODEOWNERS\nfoo/bar @some/team");
        await GitOperations.CommitAll(ProductRepoPath, "Add submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath, generateCodeowners: true);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoFilePath,
            _dependencyRepoFilePath,
            submoduleFilePath,
            VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / VmrInfo.CodeownersPath,
        };

        List<LocalPath> expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.DependencyRepoName },
            expectedFilesFromRepos
        );

        expectedFiles.Add(VmrPath / VmrInfo.CodeownersPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoFilePath, _productRepoFileName);
        CheckFileContents(_dependencyRepoFilePath, "File in dependency");
        CheckFileContents(submoduleFilePath, "File in product-repo2");
        CheckFileContents(
            VmrPath / VmrInfo.CodeownersPath,
            """
            ### CONTENT BELOW IS AUTO-GENERATED AND MANUAL CHANGES WILL BE OVERWRITTEN ###
            
            ## product-repo1 #############################################################
            
            # This is a first repo's CODEOWNERS
            /src/product-repo1/foo/bar @some/team
            """,
            removeEmptyLines: false);

        await GitOperations.CheckAllIsCommitted(VmrPath);

        // Add a file in the submodule

        await File.WriteAllTextAsync(SecondRepoPath / additionalFileName, "New external repo file");
        Directory.CreateDirectory(Path.GetDirectoryName(SecondRepoPath / VmrInfo.CodeownersPath)!);
        await File.WriteAllTextAsync(SecondRepoPath / VmrInfo.CodeownersPath, "# This is a second repo's CODEOWNERS\n/xyz/foo @other/team");
        await GitOperations.CommitAll(SecondRepoPath, "Adding new file in the submodule");
        await GitOperations.PullMain(ProductRepoPath / submoduleRelativePath);
        
        await GitOperations.CommitAll(ProductRepoPath, "Checkout submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath, generateCodeowners: true);

        expectedFiles.Add(additionalSubmoduleFilePath);
        expectedFiles.Add(submodulePathInVmr / VmrInfo.CodeownersPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(
            VmrPath / VmrInfo.CodeownersPath,
            """
            ### CONTENT BELOW IS AUTO-GENERATED AND MANUAL CHANGES WILL BE OVERWRITTEN ###
            
            ## product-repo1 #############################################################
            
            # This is a first repo's CODEOWNERS
            /src/product-repo1/foo/bar @some/team


            ## product-repo1/externals/product-repo2 #####################################
            
            # This is a second repo's CODEOWNERS
            /src/product-repo1/externals/product-repo2/xyz/foo @other/team
            """,
            removeEmptyLines: false);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // Remove submodule

        await GitOperations.RemoveSubmodule(ProductRepoPath, submoduleRelativePath);
        await File.WriteAllTextAsync(VmrPath / VmrInfo.CodeownersPath, "My new content in the CODEOWNERS\n\n### CONTENT BELOW IS AUTO-GENERATED AND MANUAL CHANGES WILL BE OVERWRITTEN ###\n");
        await GitOperations.CommitAll(ProductRepoPath, "Remove the submodule");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath, generateCodeowners: true);

        expectedFiles.Remove(submoduleFilePath);
        expectedFiles.Remove(additionalSubmoduleFilePath);
        expectedFiles.Remove(submodulePathInVmr / VmrInfo.CodeownersPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        CheckFileContents(
            VmrPath / VmrInfo.CodeownersPath,
            """
            My new content in the CODEOWNERS

            ### CONTENT BELOW IS AUTO-GENERATED AND MANUAL CHANGES WILL BE OVERWRITTEN ###

            ## product-repo1 #############################################################

            # This is a first repo's CODEOWNERS
            /src/product-repo1/foo/bar @some/team
            """,
            removeEmptyLines: false);
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
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }
}

