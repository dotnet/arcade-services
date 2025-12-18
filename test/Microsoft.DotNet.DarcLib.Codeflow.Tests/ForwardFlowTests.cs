// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class ForwardFlowTests : CodeFlowTests
{
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task OnlyForwardflowsTest(bool enableRebase)
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyForwardflowsTest);

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(enableRebase, branchName);

        // Flow again - should be a no-op
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldNotHaveUpdates();
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.DeleteBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");

        // Make a change in the repo again
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo again", branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldHaveUpdates();
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");

        // Make a conflicting change in the VMR
        codeFlowResult = await ChangeRepoFileAndFlowIt("A completely different change", branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, branchName,
            mergeTheirs: true,
            expectedConflictingFiles: [VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName],
            enableRebase: enableRebase);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");

        // We used the changes from the repo - let's verify flowing back won't change anything
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName, enableRebase: enableRebase);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
        await FinalizeBackFlow(enableRebase, branchName);

        // Now we will make a series of forward flows where each will make a conflicting change
        // The last forward flow will have to recreate all of the flows to be able to apply the changes

        // Make another flow to VMR to have flows both ways ready
        codeFlowResult = await ChangeRepoFileAndFlowIt("Again some content in the individual repo", branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(enableRebase, branchName);

        // The file.txt will keep getting changed and conflicting in each flow
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "file.txt", "VMR conflicting content");
        await GitOperations.CommitAll(VmrPath, "Set up conflicting file in VMR");

        for (int i = 1; i <= 3; i++)
        {
            await GitOperations.Checkout(ProductRepoPath, "main");
            await File.WriteAllTextAsync(ProductRepoPath / "file.txt", $"Repo content {i}");
            await GitOperations.CommitAll(ProductRepoPath, $"Add files for iteration {i}");
            codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, enableRebase: enableRebase);
            codeFlowResult.ShouldHaveUpdates();
            // Make a conflicting change in the PR branch before merging
            await File.WriteAllTextAsync(_productRepoVmrPath / $"conflicting_file_{i}.txt", $"Conflicting content {i}");
            if (enableRebase)
            {
                await GitOperations.ExecuteGitCommand(VmrPath, ["add", _productRepoVmrPath / $"conflicting_file_{i}.txt"]);
            }
            else
            {
                await GitOperations.CommitAll(VmrPath, $"Conflicting change in iteration {i}");
            }

            await GitOperations.VerifyMergeConflict(VmrPath, branchName, [VmrInfo.SourcesDir / Constants.ProductRepoName / "file.txt"], mergeTheirs: false, enableRebase: enableRebase);
            CheckFileContents(_productRepoVmrPath / "file.txt", ["VMR conflicting content"]);
        }

        // Now we create a new forward flow that will conflict with each of the previous flows
        await GitOperations.Checkout(ProductRepoPath, "main");
        for (int i = 1; i <= 3; i++)
        {
            await File.WriteAllTextAsync(ProductRepoPath / $"file_{i}.txt", $"New content {i}");
            await File.WriteAllTextAsync(ProductRepoPath / $"conflicting_file_{i}.txt", $"New content {i}");
        }
        await GitOperations.CommitAll(ProductRepoPath, "New conflicting flow");

        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, enableRebase: enableRebase);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(
            VmrPath,
            branchName,
            [
                ..Enumerable.Range(1, 3).Select(i => VmrInfo.SourcesDir / Constants.ProductRepoName / $"conflicting_file_{i}.txt"),
            ],
            mergeTheirs: true,
            enableRebase: enableRebase);

        for (int i = 1; i <= 3; i++)
        {
            CheckFileContents(_productRepoVmrPath / $"file_{i}.txt", $"New content {i}");
            CheckFileContents(_productRepoVmrPath / $"conflicting_file_{i}.txt", $"New content {i}");
        }
    }

    [Test]
    public async Task DarcVmrForwardFlowCommandTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(DarcVmrForwardFlowCommandTest);

        await File.WriteAllTextAsync(_productRepoFilePath + "-removed-in-vmr", "This file will be removed in VMR");
        await GitOperations.CommitAll(ProductRepoPath, "Add a file that will be removed in VMR");

        // We flow the repo to make sure they are in sync
        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        codeFlowResult = await ChangeVmrFileAndFlowIt("New content in the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        File.Delete(_productRepoVmrFilePath + "-removed-in-vmr");
        await GitOperations.CommitAll(VmrPath, "Remove a file that was in the repo");

        // Now we make several changes in the repo and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoFilePath, "New content in the individual repo again");
        await File.WriteAllTextAsync(_productRepoFilePath + "-added-in-repo", "New file from the repo");
        // Change an eng/common file
        File.Move(
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps1",
            ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps2");
        // Update version files
        var newDependency = new DependencyDetail
        {
            Name = "Package.New",
            Version = "1.0.1",
            RepoUri = "https://github.com/some/repo",
            Commit = "commit-sha",
            Type = DependencyType.Toolset,
        };
        await GetLocal(ProductRepoPath).AddDependencyAsync(newDependency);
        await GitOperations.CommitAll(ProductRepoPath, "New content in the individual repo again");

        string[] stagedFiles = await CallDarcForwardflow();

        // Verify that expected files are staged
        string[] expectedFiles =
        [
            VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName,
            VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName + "-added-in-repo",
            VmrInfo.SourcesDir / Constants.ProductRepoName / DarcLib.Constants.CommonScriptFilesPath / "build.ps2",
            VmrInfo.DefaultRelativeSourceManifestPath,
            VmrInfo.SourcesDir / Constants.ProductRepoName / VersionFiles.VersionDetailsXml,
            VmrInfo.SourcesDir / Constants.ProductRepoName / VersionFiles.VersionDetailsProps,
        ];

        stagedFiles.Should().BeEquivalentTo(expectedFiles);
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(VmrPath, stagedFiles);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");
        CheckFileContents(VmrPath / expectedFiles[1], "New file from the repo");
        File.Exists(VmrPath / expectedFiles[0] + "-removed-in-vmr").Should().BeFalse();
        File.Exists(VmrPath / expectedFiles[2]).Should().BeTrue();
        File.Exists(VmrPath / expectedFiles[2].Replace("ps2", "ps1")).Should().BeFalse();
        (await GetLocal(VmrPath).GetDependenciesAsync(newDependency.Name, relativeBasePath: VmrInfo.SourcesDir / Constants.ProductRepoName))
            .Should().ContainEquivalentOf(newDependency);

        // Now we reset, make a conflicting change and see if darc can handle it and the conflict appears
        await GitOperations.ExecuteGitCommand(VmrPath, "reset", "--hard");
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "A conflicting change in the VMR");
        await GitOperations.CommitAll(VmrPath, "A conflicting change in the VMR");

        var build = await CreateNewRepoBuild(
        [
            ("Package.A1", "1.0.1"),
            ("Package.B1", "1.0.1"),
            ("Package.C2", "1.0.1"),
            ("Package.D3", "1.0.1"),
        ]);

        stagedFiles = await CallDarcForwardflow(build.Id, [expectedFiles[0]]);
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(VmrPath, stagedFiles.Except([expectedFiles[0]]));
        CheckFileContents(VmrPath / expectedFiles[1], "New file from the repo");

        // Now we commit this flow and verify all files are staged
        await GitOperations.ExecuteGitCommand(VmrPath, ["checkout", "--theirs", "--", _productRepoVmrFilePath]);
        await GitOperations.ExecuteGitCommand(VmrPath, ["add", _productRepoVmrFilePath]);
        await GitOperations.ExecuteGitCommand(VmrPath, ["commit", "-m", "Committing the forward flow"]);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        (await GetLocal(VmrPath).GetDependenciesAsync(newDependency.Name, relativeBasePath: VmrInfo.SourcesDir / Constants.ProductRepoName))
            .Should().ContainEquivalentOf(newDependency);

        // Now we make another set of changes in the repo and try again
        // This time it will be same direction flow as the previous one (before it was opposite)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        File.Delete(_productRepoVmrFilePath + "-added-in-repo");
        await GitOperations.CommitAll(VmrPath, "Remove a file that was in the repo");

        // Now we make several changes in the repo and try to locally flow them via darc
        await File.WriteAllTextAsync(_productRepoFilePath, "New content in the individual repo AGAIN");
        await File.WriteAllTextAsync(_productRepoFilePath + "-added-in-repo", "New file from the repo AGAIN");
        await File.WriteAllTextAsync(ProductRepoPath / DarcLib.Constants.CommonScriptFilesPath / "build.ps2", "New stuff"); newDependency = new DependencyDetail
        {
            Name = "Package.NewNew",
            Version = "3.0.0",
            RepoUri = "https://github.com/some/repo",
            Commit = "commit-sha",
            Type = DependencyType.Toolset,
        };
        await GetLocal(ProductRepoPath).AddDependencyAsync(newDependency);
        await GitOperations.CommitAll(ProductRepoPath, "New content in the individual repo again");

        build = await CreateNewRepoBuild(
        [
            ("Package.A1", "1.0.2"),
            ("Package.B1", "1.0.2"),
            ("Package.C2", "1.0.2"),
            ("Package.D3", "1.0.2"),
        ]);

        // File -added-in-repo is deleted in the VMR and changed in the repo so it will conflict
        stagedFiles = await CallDarcForwardflow(build.Id, [expectedFiles[1]]);
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(VmrPath, stagedFiles.Except([expectedFiles[1]]));
        CheckFileContents(VmrPath / expectedFiles[1], "New file from the repo AGAIN");
        CheckFileContents(VmrPath / expectedFiles[2], "New stuff");
        (await GetLocal(VmrPath).GetDependenciesAsync(newDependency.Name, relativeBasePath: VmrInfo.SourcesDir / Constants.ProductRepoName))
            .Should().ContainEquivalentOf(newDependency);
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
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
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

        codeFlowResult = await CallBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: firstBuild,
            excludedAssets: ["Package.C2"]);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        // We flow to VMR again to level the content
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        var secondBuild = await CreateNewVmrBuild(
        [
            ("Package.A1", "3.0.0"),
            ("Package.B1", "3.0.0"),
            ("Package.C2", "3.0.0"),
            ("Package.D3", "3.0.0"),
        ]);

        codeFlowResult = await CallBackflow(
            Constants.ProductRepoName,
            ProductRepoPath,
            branchName + "-backflow",
            buildToFlow: secondBuild,
            excludedAssets: ["Package.C2"]);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "-backflow");

        // Now we try to flow forward and expect no meaningful changes to be detected
        codeFlowResult = await CallForwardflow(
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
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
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
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Now we fork the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.CreateBranch(VmrPath, "release/10.0");

        // Now we try to flow the release/10.0 commit from the repo into the VMR release/10.0 branch
        await GitOperations.Checkout(ProductRepoPath, "release/10.0");
        Func<Task> act = async () => await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "2");
        await act.Should().ThrowAsync<NonLinearCodeflowException>();
    }

    // Test that the bug https://github.com/dotnet/arcade-services/issues/5331 doesn't happen

    [Test]
    public async Task TestForwardFlowDependencyDowngradesAfterCrossingFlow()
    {
        await EnsureTestRepoIsInitialized();

        var ffBranch = nameof(TestForwardFlowDependencyDowngradesAfterCrossingFlow) + "_ff";
        var bfBranch = nameof(TestForwardFlowDependencyDowngradesAfterCrossingFlow) + "_backflow";

        // Add dependency to the repo, flow it to the VMR
        var repo = GetLocal(ProductRepoPath);
        var dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
        };

        await GitOperations.Checkout(ProductRepoPath, "main");
        await repo.AddDependencyAsync(dep);
        await GitOperations.CommitAll(ProductRepoPath, "Add Package.A1 v1.0.0");
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, ffBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, ffBranch);

        // update the dependency in the VMR, open a backflow but don't merge
        dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a011",
            Type = DependencyType.Product,
        };
        await GitOperations.Checkout(VmrPath, "main");
        var vmrRepo = GetLocal(VmrPath);
        await vmrRepo.AddDependencyAsync(dep, VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        await GitOperations.CommitAll(VmrPath, "Update Package.A1 to v1.0.1 in VMR");
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, bfBranch);
        codeFlowResult.ShouldHaveUpdates();

        // now update the same dependency again
        dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.2",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a012",
            Type = DependencyType.Product,
        };
        await GitOperations.Checkout(VmrPath, "main");
        await vmrRepo.AddDependencyAsync(dep, VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        await GitOperations.CommitAll(VmrPath, "Update Package.A1 to v1.0.2 in VMR");

        // now open and merge a forward flow
        codeFlowResult = await ChangeRepoFileAndFlowIt("Some change in the repo", ffBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, ffBranch);

        // now merge the backflow
        await GitOperations.MergePrBranch(ProductRepoPath, bfBranch);

        // now open a new forward flow, the dependency shouldn't be downgraded to 1.0.1
        codeFlowResult = await ChangeRepoFileAndFlowIt("Another change in the repo", ffBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, ffBranch);
        var deps = await vmrRepo.GetDependenciesAsync(relativeBasePath: VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        deps.Should().ContainSingle(d => d.Name == "Package.A1" && d.Version == "1.0.2");
    }

    [Test]
    public async Task PinnedDependenciesCodeFlow()
    {
        await EnsureTestRepoIsInitialized();

        const string ffBranch = nameof(PinnedDependenciesCodeFlow);
        var repo = GetLocal(ProductRepoPath);

        // Add a dependency and forward flow it
        var dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
        };
        await repo.AddDependencyAsync(dep);
        await GitOperations.CommitAll(ProductRepoPath, "Add Package.A1 v1.0.0");
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, ffBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, ffBranch);

        // Now update the dependency, while also pinning it
        dep = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "a01",
            Type = DependencyType.Product,
            Pinned = true,
        };
        await repo.AddDependencyAsync(dep, allowPinnedDependencyUpdate: true);
        await GitOperations.CommitAll(TmpPath / "product-repo1", "Update and pin Package.A1 to v1.0.1");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, ffBranch);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, ffBranch);

        // The dependency should be at 1.0.1 in the VMR now
        var vmrRepo = GetLocal(VmrPath);
        var deps = await vmrRepo.GetDependenciesAsync(relativeBasePath: VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        deps.Should().ContainSingle(d => d.Name == "Package.A1" && d.Version == "1.0.1");
    }


    /*
        This test verifies a scenario where a file is changed (added, removed, edited) and later reverted
        while there are unrelated conflicts at the same time.
        More details about this in https://github.com/dotnet/arcade-services/issues/5046

            repo                   VMR
              O────────────────────►O 0. 
              │                 2.  │\
            1.O─────────────────O   │ \__
              │                 │   │    \
              │                 └──►O 3.  \
              │                     │      O 5. FF branch
            4.O─────────────────x   │      
              │                  5. │

        Test flow:
        1. Make changes in source repo (add, remove, edit files)
        2. Forward flow these changes to target
        3. Make conflicting change directly in target
        4. Make reverts and more changes in source repo
        5. Forward flow again - this should handle reverts correctly even with conflicts
    */
    [Test]
    public async Task ForwardFlowWithRevertsAndConflictsTest()
    {
        string branchName = GetTestBranchName();

        await EnsureTestRepoIsInitialized();

        const string FileAddedAndRemovedName = "FileAddedAndRemoved.txt";
        const string FileRemovedAndAddedName = "FileRemovedAndAdded.txt";
        const string FileChangedAndPartiallyRevertedName = "FileChangedAndPartiallyReverted.txt";
        const string FileInConflictName = "FileInConflict.txt";

        const string PartialRevertOriginal =
            """
            One
            Two
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            """;

        const string PartialRevertChange1 =
            """
            One
            Two
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            111111111111
            """;

        const string PartialRevertChange2 =
            """
            One
            22222222222
            Three
            Four
            Five
            Six
            Seven
            Eight
            Nine
            Ten
            """;

        const string OriginalFileRemovedAndAddedContent = "Original content that will be removed and re-added";
        const string ConflictingContentInVmr = "Causing a conflict by a change in the target";
        const string ConflictingContentInRepo = "Causing a conflict by a change in the source repo";

        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / FileChangedAndPartiallyRevertedName, PartialRevertOriginal);
        await GitOperations.CommitAll(ProductRepoPath, "Set up file for partial revert");

        // Flow to VMR and back to populate the repo well (eng/common, the <Source /> tag..)
        var codeflowResult = await ChangeRepoFileAndFlowIt("Initial content", branchName);
        codeflowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        codeflowResult = await ChangeVmrFileAndFlowIt("Initial content in VMR", branchName);
        codeflowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Setup: Create initial file state
        await File.WriteAllTextAsync(ProductRepoPath / FileRemovedAndAddedName, OriginalFileRemovedAndAddedContent);
        await GitOperations.CommitAll(ProductRepoPath, "Add file that will be removed and re-added");

        // Step 1: Make changes in source repo
        await File.WriteAllTextAsync(ProductRepoPath / FileInConflictName, "This file will cause a conflict");
        await File.WriteAllTextAsync(ProductRepoPath / FileAddedAndRemovedName, "This file will be added and then removed");
        await File.WriteAllTextAsync(ProductRepoPath / FileChangedAndPartiallyRevertedName, PartialRevertChange1);
        File.Delete(ProductRepoPath / FileRemovedAndAddedName);

        await GitOperations.CommitAll(ProductRepoPath, "Make changes which will get reverted later", allowEmpty: false);

        var expectedFiles = new List<string>
        {
            VmrInfo.SourcesDir / Constants.ProductRepoName / FileInConflictName,
            VmrInfo.SourcesDir / Constants.ProductRepoName / FileAddedAndRemovedName,
            VmrInfo.SourcesDir / Constants.ProductRepoName / FileChangedAndPartiallyRevertedName,
            VmrInfo.DefaultRelativeSourceManifestPath,
            VmrInfo.SourcesDir / Constants.ProductRepoName / VersionFiles.VersionDetailsXml,
        };

        // Step 2: Forward flow first changes
        var stagedFiles = await CallDarcForwardflow();
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(VmrPath, stagedFiles);
        CheckFileContents(VmrPath / expectedFiles[0], "This file will cause a conflict");
        CheckFileContents(VmrPath / expectedFiles[1], "This file will be added and then removed");
        CheckFileContents(VmrPath / expectedFiles[2], PartialRevertChange1);

        // Step 3: Make a conflicting change directly in the target (simulating a change in the PR branch)
        await File.WriteAllTextAsync(_productRepoVmrPath / FileInConflictName, ConflictingContentInVmr);
        await GitOperations.CommitAll(VmrPath, "Edit files directly in target (simulating PR branch change)", allowEmpty: false);

        // Step 4: Make reverts and conflict in source repo
        await File.WriteAllTextAsync(ProductRepoPath / FileRemovedAndAddedName, OriginalFileRemovedAndAddedContent);
        await File.WriteAllTextAsync(ProductRepoPath / FileChangedAndPartiallyRevertedName, PartialRevertChange2);
        await File.WriteAllTextAsync(ProductRepoPath / FileInConflictName, ConflictingContentInRepo);
        File.Delete(ProductRepoPath / FileAddedAndRemovedName);

        await GitOperations.CommitAll(ProductRepoPath, "Revert changes", allowEmpty: false);

        // Step 5: Forward flow with reverts and conflicts
        stagedFiles = await CallDarcForwardflow(expectedConflicts: [expectedFiles[0]]);
        // Removed file is new, V.D.xml is not changed anymore
        expectedFiles[4] = VmrInfo.SourcesDir / Constants.ProductRepoName / FileRemovedAndAddedName;
        stagedFiles.Should().BeEquivalentTo(expectedFiles, "There should be staged files after forward flow");
        await Helpers.GitOperationsHelper.VerifyNoConflictMarkers(VmrPath, stagedFiles.Except([expectedFiles[0], expectedFiles[1]]));

        // Now we commit this flow and verify all files are staged
        await GitOperations.ExecuteGitCommand(VmrPath, ["checkout", "--theirs", "--", expectedFiles[0]]);
        await GitOperations.ExecuteGitCommand(VmrPath, ["add", expectedFiles[0]]);
        await GitOperations.ExecuteGitCommand(VmrPath, ["commit", "-m", "Committing the forward flow"]);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // Verify final state: The reverts should be correctly applied despite conflicts

        // FileAddedAndRemoved should not exist (was added then removed)
        File.Exists(_productRepoVmrPath / FileAddedAndRemovedName).Should().BeFalse(
            "File that was added and removed should not exist");

        // FileRemovedAndAdded should exist with original content (was removed then re-added)
        File.Exists(_productRepoVmrPath / FileRemovedAndAddedName).Should().BeTrue(
            "File that was removed and re-added should exist");
        (await File.ReadAllTextAsync(_productRepoVmrPath / FileRemovedAndAddedName)).Should().Be(
            OriginalFileRemovedAndAddedContent,
            "File should have its original content after revert");

        // FileChangedAndPartiallyReverted should have the second change
        File.Exists(_productRepoVmrPath / FileChangedAndPartiallyRevertedName).Should().BeTrue(
            "Partially reverted file should exist");

        (await File.ReadAllTextAsync(_productRepoVmrPath / FileChangedAndPartiallyRevertedName)).Should().Be(
            PartialRevertChange2 + Environment.NewLine,
            "Partially reverted file should have the second change");

        // FileInConflict should exist with the source repo's content (conflict resolved)
        File.Exists(_productRepoVmrPath / FileInConflictName).Should().BeTrue(
            "Conflicting file should exist");
        (await File.ReadAllTextAsync(_productRepoVmrPath / FileInConflictName)).Should().Be(
            ConflictingContentInRepo,
            "Conflicting file should have source repo's content after flow");
    }
}
