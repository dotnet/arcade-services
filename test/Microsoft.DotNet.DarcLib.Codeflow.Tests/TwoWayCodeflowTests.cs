// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class TwoWayCodeflowTests : CodeFlowTests
{
    [Test]
    public async Task ZigZagCodeflowTest()
    {
        const string aFileContent = "Added a new file in the repo";
        const string bFileContent = "Added a new file in the product repo in the meantime";
        const string bFileContent2 = "New content for the b file";
        string branchName = GetTestBranchName();

        await EnsureTestRepoIsInitialized();

        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "we-will-delete-this-later.txt", "And it will stay deleted");
        await GitOperations.CommitAll(VmrPath, "Added a file that will be deleted later");

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName);

        // Make some changes in the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await File.WriteAllTextAsync(ProductRepoPath / "cloaked.dll", "A cloaked file");
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);

        // Flow unrelated changes from the VMR
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR", branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Before we merge the PR branch, make a change in the product repo
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent);

        // Merge the backflow branch and verify files
        await FinalizeBackFlow(branchName);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await FinalizeBackFlow(branchName);

        // Delete a file in the VMR to make sure it's not brought back by the forward flow
        await GitOperations.Checkout(VmrPath, "main");
        File.Delete(_productRepoVmrPath / "we-will-delete-this-later.txt");
        await GitOperations.CommitAll(VmrPath, "Deleting a file in the VMR");

        // Forward flow
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent2);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent2);
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName);

        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent2);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");
        File.Exists(_productRepoVmrPath / "cloaked.dll").Should().BeFalse();
        File.Exists(_productRepoVmrPath / "we-will-delete-this-later.txt").Should().BeFalse();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        File.Exists(ProductRepoPath / "we-will-delete-this-later.txt").Should().BeFalse();
    }

    [Test]
    public async Task SubmoduleCodeFlowTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(SubmoduleCodeFlowTest);

        var submodulePath = new UnixPath("externals/external-repo");
        await GitOperations.InitializeSubmodule(ProductRepoPath, "second-repo", SecondRepoPath, submodulePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        var _submoduleFileVmrPath = _productRepoVmrPath / submodulePath / Constants.GetRepoFileName(Constants.SecondRepoName);

        var branch = await ChangeVmrFileAndFlowIt("New content in the VMR repo", branchName);
        branch.ShouldHaveUpdates();
        await FinalizeBackFlow(branchName);

        branch = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        branch.ShouldHaveUpdates();
        await GitOperations.Commit(VmrPath, "Forward flow");
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_submoduleFileVmrPath, "File in product-repo2");

        // Make an "invalid" change to the submodule in the VMR
        // This will be forbidden in the future but we need to test this
        await File.WriteAllLinesAsync(_submoduleFileVmrPath, new[] { "Invalid change" });
        await GitOperations.CommitAll(VmrPath, "Invalid change in the VMR");
        await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // In this case, a conflict in the version files will have to be auto-resolved.
    // The diagram it follows is here (O are commits):
    /*
        repo                   VMR
      AAA O────────────────────►O   
          │  2a.                │   
          │   O◄────────────────O 1a.
          │   │            4.   │   
        3.O───┼────────────►O   │   
          │2b.O◄────────────┼───O 1b.   
          │   │             │   │   
        5.O◄──┘             └──►O 6.
          │                 7.  │   
          |────────────────►O   │
          │                 └──►O 8.
          │                     │
     */
    [Test]
    public async Task ForwardFlowConflictResolutionTest()
    {
        await EnsureTestRepoIsInitialized();

        var backBranchName = GetTestBranchName();
        var forwardBranchName = GetTestBranchName(forwardFlow: true);

        // 1. Change file in VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / "1a.txt", "one");
        await GitOperations.CommitAll(VmrPath, "1a.txt");

        // 2. Open a backflow PR
        var codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "2a");

        // We make another commit in the vmr and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "1b.txt", "one again");
        await GitOperations.CommitAll(VmrPath, "1b.txt");
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "2b");

        // 3. Change file in the repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "3a.txt", "three");
        await GitOperations.CommitAll(ProductRepoPath, "3a.txt");

        // 4. Open a forward flow PR
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "4a");

        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "3b.txt", "three again");
        await GitOperations.CommitAll(ProductRepoPath, "3b.txt");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "4b");

        // 5. Merge the backflow PR
        await FinalizeBackFlow(backBranchName);

        // 6. Merge the forward flow PR
        await FinalizeForwardFlow(forwardBranchName);

        // 7. Forward flow again so the VMR version of the file will flow back to the VMR
        // While the VMR accepted the content from the repo but it will get overriden by the VMR content again
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        codeFlowResult.ConflictedFiles.Should().BeEmpty();

        // 8. Merge the forward flow PR - any conflicts in version files are dealt with automatically
        // The conflict is described in the ForwardFlowConflictResolver class
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        (string, string)[] expectedFiles =
        [
            ("1a.txt", "one"),
            ("1b.txt", "one again"),
            ("3a.txt", "three"),
            ("3b.txt", "three again"),
        ];

        foreach (var (file, content) in expectedFiles)
        {
            CheckFileContents(_productRepoVmrPath / file, content);
            CheckFileContents(ProductRepoPath / file, content);
        }

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This one simulates what would happen if a file is being changed gradually (AAA->BBB->CCC) and these changes are flowed
    // in the VMR while different backflows happen in the meantime.
    // This tests checks that the last forward flow that happens merges the target branch well to not cause conflicts.
    // This means that the PR branch created in step 8. doesn't clash with changes from step 6.
    // Technically this would happen because the branch from step 8. will be based on commit 1. (last flow source commit),
    // and the PR branch changing the file from AAA-CCC while the target branch has BBB (step 6.).
    /*
        repo                   VMR
    AAA 0.O────────────────────►O AAA
          │  2.                 │
          │   O◄────────────────O 1.
          │   │            4.   │
    BBB 3.O───┼────────────►O   │
          │   │             │   │
          │   │             │   │
        5.O◄──┘             └──►O 6. BBB
          │                 8.  │
    CCC 7.O────────────────►O   │
          │                 └──►O 9. CCC
          │                     │
     */
    [Test]
    public async Task ForwardFlowConflictWithPreviousFlowAutoResolutionTest()
    {
        await EnsureTestRepoIsInitialized();

        var backBranchName = GetTestBranchName();
        var forwardBranchName = GetTestBranchName(forwardFlow: true);

        // 0. Prepare repo and VMR
        await ChangeRepoFileAndFlowIt("AAA", forwardBranchName + "-first");
        await FinalizeForwardFlow(forwardBranchName + "-first");

        // 1. Change a different file in VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / "different-file.txt", "XXX");
        await GitOperations.CommitAll(VmrPath, "different-file.txt");

        // 2. Open a backflow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        var codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "2");

        // 3-4. Change the file in the repo again
        codeFlowResult = await ChangeRepoFileAndFlowIt("BBB", forwardBranchName + "-second");
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(VmrPath, "4");

        // 5. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

        // 6. Merge the forward flow PR
        await FinalizeForwardFlow(forwardBranchName + "-second");

        // 7-8. Update the file again in the repo
        codeFlowResult = await ChangeRepoFileAndFlowIt("CCC", forwardBranchName + "-third");
        codeFlowResult.ShouldHaveUpdates();

        // 9. Merge the forward flow PR - any conflicts are dealt with automatically
        await FinalizeForwardFlow(forwardBranchName + "-third");

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        (string, string)[] expectedFiles =
        [
            ("different-file.txt", "XXX"),
            (_productRepoFileName, "CCC"),
        ];

        foreach (var (file, content) in expectedFiles)
        {
            CheckFileContents(_productRepoVmrPath / file, content);
            CheckFileContents(ProductRepoPath / file, content);
        }

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }


    // This one simulates what would happen if a file is being changed gradually (AAA->BBB->CCC) and these changes are flowed
    // in the repo while different backflows happen in the meantime.
    // This tests checks that the last forward flow that happens merges the target branch well to not cause conflicts.
    // This means that the PR branch created in step 8. doesn't clash with changes from step 6.
    // Technically this would happen because the branch from step 8. will be based on commit 1. (last flow source commit),
    // and the PR branch changing the file from AAA-CCC while the target branch has BBB (step 6.).
    /*
        repo                   VMR
    AAA 0.O◄────────────────────O AAA
          │                2.   │
        1.O────────────────►O   │
          │  4.             │   │
          |   O◄────────────┼───O 3. BBB
          │   │             │   │
          │   │             │   │
    BBB 6.O◄──┘             └──►O 5.
          │                8.   │
          │                 O───O 7. CCC
    CCC 9.O◄────────────────┘   │
          │                     │
     */
    [Test]
    public async Task BackflowConflictWithPreviousFlowAutoResolutionTest()
    {
        await EnsureTestRepoIsInitialized();

        var backBranchName = GetTestBranchName();
        var forwardBranchName = GetTestBranchName(forwardFlow: true);

        // 0. Prepare repo and VMR
        await ChangeVmrFileAndFlowIt("AAA", backBranchName + "-first");
        await FinalizeBackFlow(backBranchName + "-first");

        // 1. Change a different file in the repo
        await File.WriteAllTextAsync(ProductRepoPath / "different-file.txt", "XXX");
        await GitOperations.CommitAll(ProductRepoPath, "different-file.txt");

        // 2. Open a forwardflow PR
        await GitOperations.Checkout(VmrPath, "main");
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(VmrPath, "2");

        // 3-4. Change the file in the VMR again
        codeFlowResult = await ChangeVmrFileAndFlowIt("BBB", backBranchName + "-second");
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "4");

        // 5. Merge the forwardflow PR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // 6. Merge the backflow PR
        await FinalizeBackFlow(backBranchName + "-second");

        // 7-8. Update the file again in the VMR
        codeFlowResult = await ChangeVmrFileAndFlowIt("CCC", backBranchName + "-third");
        codeFlowResult.ShouldHaveUpdates();

        // 9. Merge the backflow PR - any conflicts are dealt with automatically
        await FinalizeBackFlow(backBranchName + "-third");

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        (string, string)[] expectedFiles =
        [
            ("different-file.txt", "XXX"),
            (_productRepoFileName, "CCC"),
        ];

        foreach (var (file, content) in expectedFiles)
        {
            CheckFileContents(_productRepoVmrPath / file, content);
            CheckFileContents(ProductRepoPath / file, content);
        }

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // In this case, a conflict in the version files will have to be auto-resolved.
    // The diagram it follows is here (O are commits):
    /*
        repo         0.        VMR
          O◄───────────────────-O 
          │                 2.  │ 
        1.O────────────────O    │ 
          │  4.            │    │ 
          │    O───────────┼────O 3. 
          │    │           │    │ 
          │    │           │    │ 
        6.O◄───┘           └───►O 5.
          |───────────────┐     │
          │    7.         │     │ 
          │     O◄──────────────|
          |     │         │     │
       10.O   8.O      9. O────┐│
          |     │              ▼│
          │  12.O◄──────────────O 11.
       13.O◄────┘               │ 
          │                     │
          |────────────────────►O 14.
          │                     │   
     */
    [Test]
    public async Task BackwardFlowConflictResolutionTest()
    {
        await EnsureTestRepoIsInitialized();

        var backBranchName = GetTestBranchName();
        var forwardBranchName = GetTestBranchName(forwardFlow: true);
        CodeFlowResult codeFlowResult;

        // 0. Backflow of a build to populate the version files in the repo with some values
        var build = await CreateNewVmrBuild([(FakePackageName, FakePackageVersion)]);
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

        // 1. Change file in the repo
        await File.WriteAllTextAsync(ProductRepoPath / "1a.txt", "one");
        await GitOperations.CommitAll(ProductRepoPath, "1a.txt");

        // 2. Open a forward flow PR
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "2a");

        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "1b.txt", "one again");
        await GitOperations.CommitAll(ProductRepoPath, "1b.txt");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "2b");

        // 3. Change file in the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "3a.txt", "three");
        await GitOperations.CommitAll(VmrPath, "3a.txt");

        // 4. Open a backflow PR
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.1")]);
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "4a");

        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "3b.txt", "three again");
        await GitOperations.CommitAll(VmrPath, "3b.txt");
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.2")]);
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "4b");

        // 5. Merge the forward flow PR
        await FinalizeForwardFlow(forwardBranchName);

        // 6. Merge the backflow PR
        await FinalizeBackFlow(backBranchName);
        var shaInStep6 = await GitOperations.GetRepoLastCommit(ProductRepoPath);

        // 7. Flow back again so the VMR version of the file will flow back to the repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.3")]);
        backBranchName = GetTestBranchName();
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "7");

        var productRepo = GetLocal(ProductRepoPath);

        // 8. We add a new dependency in the PR branch to see if it survives the conflict
        var extraDependencyInPr = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "abc",
            Type = DependencyType.Product,
        };

        await productRepo.AddDependencyAsync(extraDependencyInPr);
        await GitOperations.CommitAll(ProductRepoPath, "Adding a new dependency");

        // 9. We flow forward a commit 6. which contains version updates from 3.
        build = await CreateNewRepoBuild([], shaInStep6);
        forwardBranchName = GetTestBranchName(forwardFlow: true);
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "9");

        // 10. We make another change on the target branch in the repo
        var newDependencyInRepo = new DependencyDetail
        {
            Name = "Package.B2",
            Version = "5.0.4",
            RepoUri = "https://github.com/dotnet/repo2",
            Commit = "def",
            Type = DependencyType.Product,
        };
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "1c.txt", "one one");
        await productRepo.AddDependencyAsync(newDependencyInRepo);
        await GitOperations.CommitAll(ProductRepoPath, "Change in main in the meantime");

        // 11/12. We flow the latest update from the repo back into the open PR
        // This is a problematic situation because version files in 7. are already updated with the packages built in 5
        // This means there might be a conflict between these and we need to override what is in the repo
        await FinalizeForwardFlow(forwardBranchName);
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.5")]);
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        // 13. Merge the backflow PR - any conflicts in version files are dealt with automatically
        // The conflict is described in the BackwardFlowConflictResolver class
        await FinalizeBackFlow(backBranchName);

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        (string, string)[] expectedFiles =
        [
            ("1a.txt", "one"),
            ("1b.txt", "one again"),
            ("1c.txt", "one one"),
            ("3a.txt", "three"),
            ("3b.txt", "three again"),
        ];

        // 14. Level the repos so that we can verify the contents
        forwardBranchName = GetTestBranchName(forwardFlow: true);
        await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        await FinalizeForwardFlow(forwardBranchName);

        foreach (var (file, content) in expectedFiles)
        {
            CheckFileContents(_productRepoVmrPath / file, content);
            CheckFileContents(ProductRepoPath / file, content);
        }

        List<DependencyDetail> expectedDependencies =
        [
            ..GetDependencies(build),
            extraDependencyInPr,
            newDependencyInRepo,
        ];

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await VerifyDependenciesInRepo(ProductRepoPath, expectedDependencies);
        await VerifyDependenciesInVmrRepo(Constants.ProductRepoName, expectedDependencies);
    }

    // This one simulates what would happen if PR both ways are open and the one that was open later merges first.
    // The diagram it follows is here (O are commits, x are conflicts):
    /*
        repo                   VMR
          O────────────────────►O   
          │  2a.                │   
          │   O◄────────────────O 1.
          │2c.O◄────────────────O 2b.
          │   │           4a.   │   
        3.O───┼────────────►O   │   
       4b.O───┼────────────►O4c.│   
          │   │             │   │   
          │ x─┘ 5.          x   │   
          │ │               │   │   
        6.O◄┘               └──►O 7.
          │                     │   
        8.O◄────────────────────│ 
          │────────────────────►O 9.
          │                     │   
     */
    [Test]
    public async Task OutOfOrderMergesWithConflictsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string aFileContent = "Added a new file in the repo";
        const string bFileContent = "Added a new file in the VMR";
        string backBranchName = GetTestBranchName();
        string forwardBranchName = GetTestBranchName(forwardFlow: true);

        // Do a forward flow once and merge so we have something to fall back on
        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardBranchName);

        // 1. Change file in VMR
        // 2a. Open a backflow PR
        await File.WriteAllTextAsync(_productRepoVmrPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(VmrPath, bFileContent);
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR #1", backBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "2a");

        // 2b. We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR #2", backBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.CommitAll(ProductRepoPath, "2c");

        // 3. Change file in the repo
        // 4a. Open a forward flow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content from the individual repo #1", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // We have a conflict - repo has the content from step 3 but VMR has the one from step 1
        // The forward flow PR will have a conflict the opposite way - repo has the content from step 4b but VMR has the one from step 1
        await GitOperations.ExecuteGitCommand(VmrPath, "checkout", "--theirs", _productRepoVmrFilePath);
        await GitOperations.CommitAll(VmrPath, "4b");

        // 4b / 4c. We make another commit in the repo and add it to the PR branch
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content from the individual repo #2", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #2");
        await GitOperations.CommitAll(VmrPath, "4c");

        // 5. The backflow PR is now in conflict - repo has the content from step 3 but VMR has the one from step 1
        // 6. We resolve the conflict by using the content from the VMR
        await GitOperations.VerifyMergeConflict(
            ProductRepoPath,
            backBranchName,
            mergeTheirs: true,
            expectedConflictingFiles: [_productRepoFileName],
            changesStagedOnly: false /* intentional, we commit everything */);
        CheckFileContents(_productRepoFilePath, "New content from the VMR #2");

        // 7. We resolve the conflict by using the content from the VMR too
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #2");

        // 8. / 9. We resolved the files to something else in each side so now we try a forward flow and a backflow
        // Where each flow should ideally bring the file to its version on both sides
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");

        var repoShaBefore = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        var vmrShaBefore = await GitOperations.GetRepoLastCommit(VmrPath);

        // Do a backflow and verify
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await GitOperations.VerifyMergeConflict(ProductRepoPath, backBranchName,
            mergeTheirs: true,
            expectedConflictingFiles: [_productRepoFileName]);

        CheckFileContents(_productRepoFilePath, "New content from the individual repo #2");
        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #2");

        // Reset to the SHAs before the conflict resolution
        await GitOperations.Checkout(ProductRepoPath, repoShaBefore);
        await GitOperations.Checkout(VmrPath, vmrShaBefore);

        await GitOperations.CreateBranch(ProductRepoPath, "main");
        await GitOperations.CreateBranch(VmrPath, "main");

        // Do a forward flow and verify
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content from the individual repo #3", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        await FinalizeForwardFlow(forwardBranchName);

        CheckFileContents(_productRepoFilePath, "New content from the individual repo #3");
        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #3");

        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
    }

    // This repo simulates frequent changes in the Version.Details.xml file.
    // It tests how updates to different packages would (not) conflict with each other.
    [Test]
    public async Task VersionDetailsConflictTest()
    {
        string branchName = GetTestBranchName();

        await EnsureTestRepoIsInitialized();

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

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.C2",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo2",
            Commit = "c03",
            Type = DependencyType.Product,
        });

        await repo.AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.D3",
            Version = "1.0.0",
            RepoUri = "https://github.com/dotnet/repo3",
            Commit = "d04",
            Type = DependencyType.Product,
        });

        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionDetailsXml,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Dependencies>
              <ProductDependencies>
                <!-- Dependencies from https://github.com/dotnet/repo1 -->
                <Dependency Name="Package.A1" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo1</Uri>
                  <Sha>a01</Sha>
                </Dependency>
                <Dependency Name="Package.B1" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo1</Uri>
                  <Sha>b02</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo1 -->
                <!-- Dependencies from https://github.com/dotnet/repo2 -->
                <Dependency Name="Package.C2" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo2</Uri>
                  <Sha>c03</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo2 -->
                <!-- Dependencies from https://github.com/dotnet/repo3 -->
                <Dependency Name="Package.D3" Version="1.0.0">
                  <Uri>https://github.com/dotnet/repo3</Uri>
                  <Sha>d04</Sha>
                </Dependency>
                <!-- End of dependencies from https://github.com/dotnet/repo3 -->
              </ProductDependencies>
              <ToolsetDependencies />
            </Dependencies>
            """);

        // The Versions.props file intentionally contains padding comment lines like in real repos
        // These lines make sure that neighboring lines are not getting in conflict when used as context during patch application
        // Repos like SDK have figured out that this is a good practice to avoid conflicts in the version files
        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionsProps,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <MSBuildAllProjects>$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
              </PropertyGroup>
              <PropertyGroup>
                <VersionPrefix>9.0.100</VersionPrefix>
              </PropertyGroup>
              <!-- Dependencies from https://github.com/dotnet/repo1 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo1-->
                <PackageA1PackageVersion>1.0.0</PackageA1PackageVersion>
                <PackageB1PackageVersion>1.0.0</PackageB1PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo1 -->
              <!-- Dependencies from https://github.com/dotnet/repo2 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo2-->
                <PackageC2PackageVersion>1.0.0</PackageC2PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo2 -->
              <!-- Dependencies from https://github.com/dotnet/repo3 -->
              <PropertyGroup>
                <!-- Dependencies from https://github.com/dotnet/repo3 -->
                <PackageD3PackageVersion>1.0.0</PackageD3PackageVersion>
              </PropertyGroup>
              <!-- End of dependencies from https://github.com/dotnet/repo3 -->
            </Project>
            """);

        // Level the repo and the VMR
        await GitOperations.CommitAll(ProductRepoPath, "Changing version files");
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName);

        // Update repo1 and repo3 dependencies in the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GetLocal(ProductRepoPath).UpdateDependenciesAsync(
            [
                new DependencyDetail
                {
                    Name = "Package.A1",
                    Version = "1.0.1",
                    RepoUri = "https://github.com/dotnet/repo1",
                    Commit = "abc",
                },
                new DependencyDetail
                {
                    Name = "Package.B1",
                    Version = "1.0.1",
                    RepoUri = "https://github.com/dotnet/repo1",
                    Commit = "abc",
                },
                new DependencyDetail
                {
                    Name = "Package.D3",
                    Version = "1.0.3",
                    RepoUri = "https://github.com/dotnet/repo3",
                    Commit = "def",
                },
            ],
            remoteFactory: null,
            ServiceProvider.GetRequiredService<IGitRepoFactory>(),
            Mock.Of<IBarApiClient>());

        await GitOperations.CommitAll(ProductRepoPath, "Update repo1 and repo3 dependencies in the product repo");

        var vmrVersionDetails = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionDetailsXml);
        var vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionsProps);

        // Update repo2 dependencies in the VMR
        vmrVersionDetails = vmrVersionDetails
            .Replace(@"Package.C2"" Version=""1.0.0""", @"Package.C2"" Version=""2.0.0""")
            .Replace("<Sha>c03</Sha>", "<Sha>c04</Sha>");

        vmrVersionProps = vmrVersionProps
            .Replace("PackageC2PackageVersion>1.0.0", "PackageC2PackageVersion>2.0.0");

        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionDetailsXml, vmrVersionDetails);
        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionsProps, vmrVersionProps);
        await GitOperations.CommitAll(VmrPath, "Update repo2 dependencies in the VMR");

        // Flow repo to the VMR
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "2");
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName + "2");

        // Flow changes back from the VMR
        var build = await CreateNewVmrBuild([("Package.A1", "1.0.20")]);
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "3", build);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeBackFlow(branchName + "3");

        // Verify the version files have both of the changes
        List<DependencyDetail> expectedDependencies =
        [
            new()
            {
                // Update 1.0.20 comes from the build
                Name = "Package.A1",
                Version = "1.0.20",
                RepoUri = build.GetRepository(),
                Commit = build.Commit,
                Type = DependencyType.Product,
            },
            new()
            {
                // Update comes from the repo
                Name = "Package.B1",
                Version = "1.0.1",
                RepoUri = "https://github.com/dotnet/repo1",
                Commit = "abc",
                Type = DependencyType.Product,
            },
            new()
            {
                // Update to 2.0.0 happened in the VMR
                Name = "Package.C2",
                Version = "2.0.0",
                RepoUri = "https://github.com/dotnet/repo2",
                Commit = "c04",
                Type = DependencyType.Product,
            },
            new()
            {
                // Update comes from the repo
                Name = "Package.D3",
                Version = "1.0.3",
                RepoUri = "https://github.com/dotnet/repo3",
                Commit = "def",
                Type = DependencyType.Product,
            },
        ];

        await VerifyDependenciesInRepo(ProductRepoPath, expectedDependencies);

        // Flow repo to the VMR
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "4");
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(branchName + "4");

        new VersionDetailsParser()
            .ParseVersionDetailsFile(_productRepoVmrPath / VersionFiles.VersionDetailsXml)
            .Dependencies
            .Should().BeEquivalentTo(expectedDependencies);

        vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionsProps);
        CheckFileContents(ProductRepoPath / VersionFiles.VersionsProps, expected: vmrVersionProps);
    }

    /*
         This test verifies a scenario where a file is added and later reverted
         while there are unrelated conflicts at the same time.
            repo                   VMR
              O────────────────────►O 0. 
              │                 2.  │
            1.O─────────────────O   │
              │                 │   │
              │                 └──►O 3.
              │                     │
            4.O─────────────────x   │
              │                  5. │

        0. Repo gets a file partial-revert.txt and flows into the VMR
        1. Two files (conflict.txt, full-revert.txt) are added in the repo
        2. FF is opened and in the PR branch, we change conflict.txt to something
        3. FF PR is merged
        4. Three changes are made in the repo:
           - The full-revert.txt file is deleted
           - The partial-revert.txt file is reverted to its original form
           - The conflict.txt is changed to something else (which will conflict later)
        5. The next forward flow will conflict over the conflict.txt file
           This means the FF branch will be based on 0.
           This means the FF branch needs to have all the changes from the repo (1-4)
           BUT the revert files won't be part of the changes because going from 1 to 4, they don't have any changes
           That means the PR branch won't have those change and will stay unchanged the VMR after the PR
    */
    [Test]
    public async Task RevertingFileAndConflictsForwardFlowTest()
    {
        const string branchName = nameof(RevertingFileAndConflictsForwardFlowTest);
        const string conflictFileContent1 = "Initial conflict file content";
        const string conflictFileContent2 = "Modified conflict file content in PR";
        const string conflictFileContent3 = "Final conflict file content";
        const string revertFileContent = "This file will be reverted";

        await EnsureTestRepoIsInitialized();

        // 0. Repo gets a file partial-revert.txt and flows into the VMR
        await File.WriteAllTextAsync(ProductRepoPath / "partial-revert.txt", revertFileContent);
        await GitOperations.CommitAll(ProductRepoPath, "Add partial-revert.txt files");
        var codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // 1. Two files (conflict.txt and full-revert.txt) are added in the repo, partial-revert.txt is changed
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "conflict.txt", conflictFileContent1);
        await File.WriteAllTextAsync(ProductRepoPath / "full-revert.txt", revertFileContent);
        await File.WriteAllTextAsync(ProductRepoPath / "partial-revert.txt", conflictFileContent1);
        await GitOperations.CommitAll(ProductRepoPath, "Add conflict.txt and full-revert.txt files, change partial-revert.txt");

        // 2. FF is opened and in the PR branch, we change conflict.txt to something
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Modify conflict.txt in the PR branch (VMR)
        await GitOperations.Checkout(VmrPath, branchName);
        await File.WriteAllTextAsync(_productRepoVmrPath / "conflict.txt", conflictFileContent2);
        await GitOperations.CommitAll(VmrPath, "Modify conflict.txt in PR branch");

        // 3. FF PR is merged
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Verify all files are in the VMR
        CheckFileContents(_productRepoVmrPath / "conflict.txt", conflictFileContent2);
        CheckFileContents(_productRepoVmrPath / "partial-revert.txt", conflictFileContent1);
        CheckFileContents(_productRepoVmrPath / "full-revert.txt", revertFileContent);

        // 4. The revert files are reverted, the conflict.txt is changed to something else
        await GitOperations.Checkout(ProductRepoPath, "main");
        File.Delete(ProductRepoPath / "full-revert.txt");
        await File.WriteAllTextAsync(ProductRepoPath / "partial-revert.txt", revertFileContent);
        await GitOperations.CommitAll(ProductRepoPath, "Revert files");

        // Change conflict.txt to something else in the repo
        await File.WriteAllTextAsync(ProductRepoPath / "conflict.txt", conflictFileContent3);
        await GitOperations.CommitAll(ProductRepoPath, "Change conflict.txt to final content");

        // 5. The next forward flow will conflict over the conflict.txt file
        // This means the FF branch will be based on 0.
        // This means the FF branch needs to have all the changes from the repo (1-4)
        // BUT the full-revert.txt won't be part of the changes because it was reverted
        // That means the PR branch won't remove it and it will stay in the VMR (even after we resolve the conflict)
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Verify we have a conflict and resolve it using theirs (repo content)
        await GitOperations.VerifyMergeConflict(VmrPath, branchName,
            mergeTheirs: true,
            expectedConflictingFiles: [VmrInfo.SourcesDir / Constants.ProductRepoName / "conflict.txt"]);

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // After resolving the conflict, verify the state:
        // - conflict.txt should have the repo's final content
        // - partial-revert.txt should have the original content
        // - full-revert.txt should still exist in the VMR (because it wasn't part of the reverted changes)
        CheckFileContents(_productRepoVmrPath / "conflict.txt", conflictFileContent3);
        CheckFileContents(_productRepoVmrPath / "partial-revert.txt", revertFileContent);
        File.Exists(ProductRepoPath / "full-revert.txt").Should().BeFalse();
    }

    // Same test as above but mirrored.
    [Test]
    public async Task RevertingFileAndConflictsBackflowTest()
    {
        const string branchName = nameof(RevertingFileAndConflictsBackflowTest);
        const string conflictFileContent1 = "Initial conflict file content";
        const string conflictFileContent2 = "Modified conflict file content in PR";
        const string conflictFileContent3 = "Final conflict file content";
        const string revertFileContent = "This file will be reverted";

        await EnsureTestRepoIsInitialized();

        // 0. VMR gets a file partial-revert.txt and flows back to the repo
        await File.WriteAllTextAsync(_productRepoVmrPath / "partial-revert.txt", revertFileContent);
        await GitOperations.CommitAll(VmrPath, "Add partial-revert.txt files");
        var codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // 1. Two files (conflict.txt, full-revert.txt) are added in the VMR, partial-revert.txt is changed
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "conflict.txt", conflictFileContent1);
        await File.WriteAllTextAsync(_productRepoVmrPath / "full-revert.txt", revertFileContent);
        await File.WriteAllTextAsync(_productRepoVmrPath / "partial-revert.txt", conflictFileContent1);
        await GitOperations.CommitAll(VmrPath, "Add conflict.txt and full-revert.txt files, change partial-revert.txt");

        // 2. Backflow PR is opened and in the PR branch, we change conflict.txt to something
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Modify conflict.txt in the PR branch (repo)
        await GitOperations.Checkout(ProductRepoPath, branchName);
        await File.WriteAllTextAsync(ProductRepoPath / "conflict.txt", conflictFileContent2);
        await GitOperations.CommitAll(ProductRepoPath, "Modify conflict.txt in PR branch");

        // 3. Backflow PR is merged
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Verify all files are in the repo
        CheckFileContents(ProductRepoPath / "conflict.txt", conflictFileContent2);
        CheckFileContents(ProductRepoPath / "partial-revert.txt", conflictFileContent1);
        CheckFileContents(ProductRepoPath / "full-revert.txt", revertFileContent);

        // 4. The revert files are reverted, the conflict.txt is changed to something else
        await GitOperations.Checkout(VmrPath, "main");
        File.Delete(_productRepoVmrPath / "full-revert.txt");
        await File.WriteAllTextAsync(_productRepoVmrPath / "partial-revert.txt", revertFileContent);
        await GitOperations.CommitAll(VmrPath, "Revert files");

        // Change conflict.txt to something else in the VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / "conflict.txt", conflictFileContent3);
        await GitOperations.CommitAll(VmrPath, "Change conflict.txt to final content");

        // 5. The next backflow will conflict over the conflict.txt file
        // This means the backflow branch will be based on 0.
        // This means the backflow branch needs to have all the changes from the VMR (1-4)
        // BUT the full-revert.txt won't be part of the changes because it was reverted
        // That means the PR branch won't have those changes and will stay unchanged in the repo after the PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Verify we have a conflict and resolve it using theirs (VMR content)
        await GitOperations.VerifyMergeConflict(ProductRepoPath, branchName,
            mergeTheirs: true,
            expectedConflictingFiles: ["conflict.txt"]);

        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // After resolving the conflict, verify the state:
        // - conflict.txt should have the VMR's final content
        // - partial-revert.txt should have the original content
        // - full-revert.txt should still exist in the repo (because it wasn't part of the reverted changes)
        CheckFileContents(ProductRepoPath / "conflict.txt", conflictFileContent3);
        CheckFileContents(ProductRepoPath / "partial-revert.txt", revertFileContent);
        File.Exists(ProductRepoPath / "full-revert.txt").Should().BeFalse();
    }

    // This test verifies that backflows work if the target repo if since tha last backflow, the product repo has added or removed dependencies,
    // while the VMR did the opposite
    [Test]
    public async Task BackflowingConflictingDependenciesWorks()
    {
        const string forwardBranchName = nameof(BackflowingConflictingDependenciesWorks);
        const string backfBranchName = nameof(BackflowingConflictingDependenciesWorks) + "-back";

        await EnsureTestRepoIsInitialized();

        var productRepo = GetLocal(ProductRepoPath);
        var vmrRepo = GetLocal(VmrPath);

        var firstDependency = new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "abc",
            Type = DependencyType.Product,
        };
        var secondDependency = new DependencyDetail
        {
            Name = "Package.B1",
            Version = "1.0.1",
            RepoUri = "https://github.com/dotnet/repo1",
            Commit = "abc",
            Type = DependencyType.Product,
        };

        // Add a new dependency
        await GitOperations.Checkout(ProductRepoPath, "main");
        await productRepo.AddDependencyAsync(firstDependency);
        await GitOperations.CommitAll(ProductRepoPath, "Adding a new dependency in the PR branch");

        // forward flow it and merge
        var codeFlowResult = await ChangeRepoFileAndFlowIt("not important", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await FinalizeForwardFlow(forwardBranchName);

        // now open a backflow, but don't merge
        codeFlowResult = await ChangeVmrFileAndFlowIt("not important1", backfBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // remove the first dependency and add a second one in the backflow PR and merge
        await productRepo.RemoveDependencyAsync("Package.A1");
        await productRepo.AddDependencyAsync(secondDependency);
        await GitOperations.CommitAll(ProductRepoPath, "Removing the dependency in the backflow PR");
        await GitOperations.MergePrBranch(ProductRepoPath, backfBranchName);

        // now add the first dependency back and remove the second one from the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await productRepo.AddDependencyAsync(firstDependency);
        await productRepo.RemoveDependencyAsync("Package.B1");
        await GitOperations.CommitAll(ProductRepoPath, "Adding the dependency back in the main branch");

        // remove the first dependency from the VMR and add the second one
        await GitOperations.Checkout(VmrPath, "main");
        await vmrRepo.AddDependencyAsync(secondDependency, VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        await vmrRepo.RemoveDependencyAsync("Package.A1", VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName));
        await GitOperations.CommitAll(VmrPath, "Removing the dependency in the VMR repo");

        // and now backflow, this will cause a conflict
        codeFlowResult = await ChangeVmrFileAndFlowIt("not important2", backfBranchName);

        codeFlowResult.ShouldHaveUpdates();
        // both of the changes were conflicting, so we took whatever the target branch had, resulting in no updates
        codeFlowResult.DependencyUpdates.Should().BeEmpty();
        var comments = GetLastFlowCollectedComments();
        comments.Should().HaveCountGreaterThanOrEqualTo(2);
        comments.Should().Contain(c => c.Contains($"There was a conflict when merging version properties. In file eng/Version.Details.xml, property 'Package.B1'{Environment.NewLine}was removed in the target branch but added in the source repo."));
        comments.Should().Contain(c => c.Contains($"There was a conflict when merging version properties. In file eng/Version.Details.xml, property 'Package.A1'{Environment.NewLine}was added in the target branch but removed in the source repo's branch."));
    }

    /*
         When not using forceUpdate, codeflow PRs on the target repository should stop receiving additional 
         flows as soon as an opposite-flow is merged on the source repository. 
         Once the source repository has received new changes from the target repository, the opposite-flow codeflow must be used,
         but it is impossible to apply opposite-flow on an existing PR.
     
            repo                   VMR   
              │                     │   
              │   O◄────────────────O 0.
              │   │                 │   
           1. O───┼────────────────►O  
              │   │                 │   
              │   x◄────────────────O   
              │ 2.                  │   
              │                     │   

        0. VMR gets a file VMR.txt and flows it into the repo without merging
        1. Repo gets a file Repo.txt and flows it into the VMR, and merges it
        2. VMR makes a first modification to VMR.txt and flows it - this should fail with a codeflow exception
    */
    [Test]
    public async Task BlockCodeflowPrWhenOppositeFlowIsMerged()
    {
        const string branchName = nameof(BlockCodeflowPrWhenOppositeFlowIsMerged);

        await EnsureTestRepoIsInitialized();

        // 0.VMR gets a file VMR.txt and flows it into the repo without merging
        await File.WriteAllTextAsync(_productRepoVmrPath / "A.txt", "1");
        await GitOperations.CommitAll(VmrPath, "1");
        var codeFlowResult = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();

        // 1.Repo gets a file Repo.txt and flows it into the VMR, and merges it
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "B.txt", "Hello world");
        await GitOperations.CommitAll(ProductRepoPath, "Hello world");
        codeFlowResult = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, forceUpdate: true);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // 2.VMR makes a first modification to VMR.txt and flows it - this should fail with a codeflow exception
        await File.WriteAllTextAsync(_productRepoVmrPath / "A.txt", "2");
        await GitOperations.CommitAll(VmrPath, "2");

        Assert.ThrowsAsync<BlockingCodeflowException>(async () =>
            await CallBackflow(
                Constants.ProductRepoName,
                ProductRepoPath,
                branchName,
                forceUpdate: false));
    }
}
