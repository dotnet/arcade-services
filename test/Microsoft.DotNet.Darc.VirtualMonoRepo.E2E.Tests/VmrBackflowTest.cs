// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrBackflowTest :  VmrTestsBase
{
    private readonly string _productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
    private NativePath _productRepoVmrPath = null!;
    private NativePath _productRepoVmrFilePath = null!;
    private NativePath _productRepoFilePath = null!;

    [SetUp]
    public void SetUp()
    {
        _productRepoVmrPath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName;
        _productRepoVmrFilePath = _productRepoVmrPath / _productRepoFileName;
        _productRepoFilePath = ProductRepoPath / _productRepoFileName;
    }

    [Test]
    public async Task FileAreBackflownTest()
    {
        await EnsureTestRepoIsInitialized();

        File.WriteAllText(ProductRepoPath / _productRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a file in the repo");

        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName],
            [_productRepoVmrFilePath]
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoVmrFilePath, "Test changes in repo file");
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // Make a change in the VMR
        File.WriteAllText(_productRepoVmrPath / _productRepoFileName, "New content from the VMR");
        await GitOperations.CommitAll(VmrPath, "Changing a file in the VMR");

        // Backflow
        var branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);
        CheckFileContents(_productRepoVmrFilePath, "New content from the VMR");
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Backflow again - should be a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

        // Make a change in the VMR again
        File.WriteAllText(_productRepoVmrPath / _productRepoFileName, "New content from the VMR again");
        await GitOperations.CommitAll(VmrPath, "Changing a file in the VMR again");

        // Second backflow in a row
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNullOrEmpty();
        CheckFileContents(_productRepoVmrFilePath, "New content from the VMR again");
        CheckFileContents(_productRepoFilePath, "New content from the VMR again");

        // Make an additional change in the PR branch before merging
        File.WriteAllText(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");

        await GitOperations.MergePrBranch(ProductRepoPath, branch!);
        CheckFileContents(_productRepoFilePath, "Change that happened in the PR");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        Dictionary<string, List<string>> dependenciesMap = [];

        await CopyRepoAndCreateVersionDetails(
            CurrentTestDirectory,
            Constants.ProductRepoName,
            dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings =
            [
                new()
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath,
                }
            ]
        };

        sourceMappings.Defaults.Exclude =
        [
            "externals/external-repo/**/*.exe", 
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        ];

        await WriteSourceMappingsInVmr(sourceMappings);
    }

    private async Task EnsureTestRepoIsInitialized()
    {
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _productRepoVmrFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName],
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(_productRepoVmrFilePath, _productRepoFileName);
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }
}

