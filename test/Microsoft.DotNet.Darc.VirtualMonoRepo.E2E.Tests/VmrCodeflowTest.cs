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
public class VmrCodeflowTest :  VmrTestsBase
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
    public async Task OnlyBackflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        var branch = await ChangeVmrFileAndFlowIt("New content from the VMR");
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Backflow again - should be a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

        // Make a change in the VMR again
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR again");
        branch.Should().NotBeNullOrEmpty();

        // Make an additional change in the PR branch before merging
        File.WriteAllText(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Make a conflicting change in the VMR
        branch = await ChangeVmrFileAndFlowIt("A completely different change");
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branch!, expectedFileInConflict: _productRepoFileName);
    }

    [Test]
    public async Task OnlyForwardflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        var branch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Backflow again - should be a no-op
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();

        // Make a change in the repo again
        branch = await ChangeRepoFileAndFlowIt("New content in the individual repo again");
        branch.Should().NotBeNullOrEmpty();

        // Make an additional change in the PR branch before merging
        File.WriteAllText(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // TODO: One more backflow that will have a conflict
    }

    [Test]
    public async Task ZigZagCodeflowTest()
    {
        await EnsureTestRepoIsInitialized();

        var branch = await ChangeRepoFileAndFlowIt("New content in the individual repo");
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Make a change in the VMR
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR");
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Make a change in the VMR again
        branch = await ChangeVmrFileAndFlowIt("New content from the VMR again");
        branch.Should().NotBeNullOrEmpty();

        // Make an additional change in the PR branch before merging
        File.WriteAllText(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branch!);

        // Forward flow
        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().NotBeNullOrEmpty();
        await GitOperations.MergePrBranch(VmrPath, branch!);

        // Backflow - should be a no-op
        branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        branch.Should().BeNull();
    }

    private async Task<string?> ChangeRepoFileAndFlowIt(string newContent)
    {
        File.WriteAllText(_productRepoFilePath, newContent);
        await GitOperations.CommitAll(ProductRepoPath, $"Changing a repo file to '{newContent}'", true);

        var branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath);
        CheckFileContents(_productRepoVmrFilePath, newContent);
        return branch;
    }

    private async Task<string?> ChangeVmrFileAndFlowIt(string newContent)
    {
        File.WriteAllText(_productRepoVmrPath / _productRepoFileName, newContent);
        await GitOperations.CommitAll(VmrPath, $"Changing a VMR file to '{newContent}'", true);

        var branch = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath);
        CheckFileContents(_productRepoFilePath, newContent);
        return branch;
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

        File.WriteAllText(ProductRepoPath / _productRepoFileName, "Test changes in repo file");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a file in the repo");

        // Perform last VMR-lite-like forward flow
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName],
            [_productRepoVmrFilePath]
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(_productRepoVmrFilePath, "Test changes in repo file");
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }
}

