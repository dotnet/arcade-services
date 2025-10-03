// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Operations.VirtualMonoRepo;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class ForwardFlowTests : CodeFlowTests
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
        await File.WriteAllTextAsync(_productRepoFilePath + "_2", "New file from the repo");
        await GitOperations.CommitAll(ProductRepoPath, "New content in the individual repo again");

        var stagedFiles = await CallDarcForwardflow();

        // Verify that expected files are staged
        string[] expectedFiles =
        [
            VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName,
            VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName + "_2",
            VmrInfo.DefaultRelativeSourceManifestPath
        ];
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");
        CheckFileContents(VmrPath / expectedFiles[1], "New file from the repo");

        // Now we reset, make a conflicting change and see if darc can handle it and the conflict appears
        await GitOperations.ExecuteGitCommand(VmrPath, "reset", "--hard");
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "A conflicting change in the VMR");
        await GitOperations.CommitAll(VmrPath, "A conflicting change in the VMR");

        stagedFiles = await CallDarcForwardflow();
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");

        // Verify that a file is in a conflicted state
        (await GitOperations.ExecuteGitCommand(VmrPath, ["diff", "--name-status"])).StandardOutput.Should().MatchRegex(@$"^U\s+{Regex.Escape(expectedFiles[0])}");
        (await File.ReadAllTextAsync(_productRepoVmrFilePath)).Should().Contain(">>>>>");
        CheckFileContents(VmrPath / expectedFiles[1], "New file from the repo");

        await GitOperations.ExecuteGitCommand(VmrPath, ["add", "--", .. expectedFiles]);
        await GitOperations.CommitAll(VmrPath, "Commit staged files");
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }

    [Test]
    public async Task MeaninglessChangesAreSkippedTest()
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
        const string branchName = nameof(MeaninglessChangesAreSkippedTest);
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
            forceUpdate: false);
        codeFlowResult.ShouldNotHaveUpdates();
    }

    // Tests a scenario where the repo and VMR both fork (a release branch snap for instance)
    // but the repo forked too soon and the new VMR branch already has changes from the main branch inside that the repo branch does not.
    // The commit we'd try to flow from the repo then would not be a descendant of the previously flown commit and this must not be allowed.
    // https://github.com/dotnet/arcade-services/issues/4973
    [Test]
    public async Task NonLinearForwardflowIsDetectedTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(NonLinearForwardflowIsDetectedTest);

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Flow again - should be a no-op
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldNotHaveUpdates();

        await GitOperations.Checkout(ProductRepoPath, "main");

        // Commit #1 in the release branch
        await GitOperations.CreateBranch(ProductRepoPath, "release/10.0");
        await File.WriteAllTextAsync(_productRepoFilePath, "Change in the release branch");
        await GitOperations.CommitAll(ProductRepoPath, "Change in the release branch");

        // Commit #2 in the main branch
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(_productRepoFilePath, "Change in the main branch");
        await GitOperations.CommitAll(ProductRepoPath, "Change in the main branch");

        // Commits #1 and #2 are not related
        // Now we flow the main commit into the VMR which is not forked yet
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Now we fork the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.CreateBranch(VmrPath, "release/10.0");

        // Now we try to flow the release/10.0 commit from the repo into the VMR release/10.0 branch
        await GitOperations.Checkout(ProductRepoPath, "release/10.0");
        Func<Task> act = async () => await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "2");
        await act.Should().ThrowAsync<NonLinearCodeflowException>();
    }
}

