// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class VmrForwardFlowTest : VmrCodeFlowTests
{

    [Test]
    public async Task OnlyForwardflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyForwardflowsTest);

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Flow again - should be a no-op
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldNotHaveUpdates();
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.DeleteBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");

        // Make a change in the repo again
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo again", branchName);
        codeFlowResult.ShouldHaveUpdates();
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");

        // Make a conflicting change in the VMR
        codeFlowResult = await ChangeRepoFileAndFlowIt("A completely different change", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, branchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");

        // We used the changes from the repo - let's verify flowing back won't change anything
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
    }

    [Test]
    public async Task DarcVmrForwardFlowCommandTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(DarcVmrForwardFlowCommandTest);

        // We flow the repo to make sure they are in sync
        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        codeFlowResult = await ChangeVmrFileAndFlowIt("New content in the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        // Now we make several changes in the repo and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoFilePath, "New content in the individual repo again");
        await GitOperations.CommitAll(ProductRepoPath, "New content in the individual repo again");

        var options = new ForwardFlowCommandLineOptions()
        {
            VmrPath = VmrPath,
            TmpPath = TmpPath,
        };

        var operation = ActivatorUtilities.CreateInstance<ForwardFlowOperation>(ServiceProvider, options);
        var currentDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(ProductRepoPath);
        try
        {
            var result = await operation.ExecuteAsync();
            result.Should().Be(0);
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        // Verify that expected files are staged
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");
        var processManager = ServiceProvider.GetRequiredService<IProcessManager>();

        var gitResult = await processManager.ExecuteGit(VmrPath, "diff", "--name-only", "--cached");
        gitResult.Succeeded.Should().BeTrue("Git diff should succeed");
        var stagedFiles = gitResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var expectedFile = VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName;
        stagedFiles.Should().BeEquivalentTo([expectedFile], "There should be staged files after backflow");

        gitResult = await processManager.ExecuteGit(VmrPath, "commit", "-m", "Commit staged files");
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }

    [Test]
    public async Task MeaninglessChangesAreSkipped()
    {
        await EnsureTestRepoIsInitialized();

        // Add dependencies to the product repo
        var repo = GetLocal(ProductRepoPath);
        await repo.RemoveDependencyAsync(FakePackageName);
        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
        });

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.B1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "b02",
            Type = DependencyType.Product,
        });

        await GitOperations.CommitAll(ProductRepoPath, "Set up version files");

        // Level the repo and the VMR
        const string branchName = nameof(MeaninglessChangesAreSkipped);
        var codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Now we flow a first build with no other changes (package updates only)
        // So that the <Source> tag is populated in the repo
        var firstBuild = await CreateNewVmrBuild(
            [
                ("Package.A1", "2.0.0"),
                ("Package.B1", "2.0.0"),
                ("Package.C2", "2.0.0"),
                ("Package.D3", "2.0.0"),
            ]);

        codeFlowResult = await CallDarcBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: firstBuild,
            excludedAssets: ["Package.C2"]);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        // We flow to VMR again to level the content
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        var secondBuild = await CreateNewVmrBuild(
            [
                ("Package.A1", "3.0.0"),
                ("Package.B1", "3.0.0"),
                ("Package.C2", "3.0.0"),
                ("Package.D3", "3.0.0"),
            ]);

        codeFlowResult = await CallDarcBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: secondBuild,
            excludedAssets: ["Package.C2"]);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        // Now we try to flow forward and expect no meaningful changes to be detected
        codeFlowResult = await CallDarcForwardflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName,
            // This is what we're testing in this test
            skipMeaninglessUpdates: true);
        codeFlowResult.ShouldNotHaveUpdates();
    }

    [Test]
    public async Task ForwardFlowPrChangesTest()
    {
        await EnsureTestRepoIsInitialized();

        const string forwardBranchName = nameof(ForwardFlowPrChangesTest) + "-ff";
        const string forwardBranchName2 = nameof(ForwardFlowPrChangesTest) + "-ff2";

        // Make a change in the repo and open a forward flow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "1a.txt", "one");
        await GitOperations.CommitAll(ProductRepoPath, "1a.txt");

        // Open a forward flow PR
        var codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // Make a change in the VMR forward flow PR
        await GitOperations.Checkout(VmrPath, forwardBranchName);
        File.Delete(VmrPath / VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName) / "1a.txt");
        await GitOperations.CommitAll(VmrPath, "Change in the VMR forward flow PR");

        // Merge the PR in the VMR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // Make a new change in the product repo and forward flow
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "1a.txt", "two");
        await GitOperations.CommitAll(ProductRepoPath, "1a.txt");

        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName2);
        codeFlowResult.ConflictedFiles.Count.Should().Be(3);
        codeFlowResult.ConflictedFiles.Should().Contain(VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName) / "1a.txt");
    }
}

