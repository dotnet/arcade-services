﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class VmrTwoWayCodeflowTest : VmrCodeFlowTests
{
    [Test]
    public async Task ZigZagCodeflowTest()
    {
        const string aFileContent = "Added a new file in the repo";
        const string bFileContent = "Added a new file in the product repo in the meantime";
        const string bFileContent2 = "New content for the b file";
        const string branchName = nameof(ZigZagCodeflowTest);

        await EnsureTestRepoIsInitialized();

        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "we-will-delete-this-later.txt", "And it will stay deleted");
        await GitOperations.CommitAll(VmrPath, "Added a file that will be deleted later");

        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Make some changes in the product repo
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
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
        CheckFileContents(ProductRepoPath / "a.txt", aFileContent);
        CheckFileContents(ProductRepoPath / "b.txt", bFileContent);
        CheckFileContents(_productRepoFilePath, "New content from the VMR");

        // Make a change in the VMR again
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR again", branchName);
        codeFlowResult.ShouldHaveUpdates();

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        // Delete a file in the VMR to make sure it's not brought back by the forward flow
        await GitOperations.Checkout(VmrPath, "main");
        File.Delete(_productRepoVmrPath / "we-will-delete-this-later.txt");
        await GitOperations.CommitAll(VmrPath, "Deleting a file in the VMR");

        // Forward flow
        await File.WriteAllTextAsync(ProductRepoPath / "b.txt", bFileContent2);
        await GitOperations.CommitAll(ProductRepoPath, bFileContent2);
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrPath / "a.txt", aFileContent);
        CheckFileContents(_productRepoVmrPath / "b.txt", bFileContent2);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");
        File.Exists(_productRepoVmrPath / "cloaked.dll").Should().BeFalse();
        File.Exists(_productRepoVmrPath / "we-will-delete-this-later.txt").Should().BeFalse();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);

        // Backflow - should be a no-op
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);
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
        await GitOperations.MergePrBranch(ProductRepoPath, branchName);

        branch = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        branch.ShouldHaveUpdates();
        await GitOperations.CheckAllIsCommitted(VmrPath);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_submoduleFileVmrPath, "File in product-repo2");

        // Make an "invalid" change to the submodule in the VMR
        // This will be forbidden in the future but we need to test this
        await File.WriteAllLinesAsync(_submoduleFileVmrPath, new[] { "Invalid change" });
        await GitOperations.CommitAll(VmrPath, "Invalid change in the VMR");
        await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
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
          │  2.                 │   
          │   O◄────────────────O 1.
          │   │            4.   │   
        3.O───┼────────────►O   │   
          │   │             │   │   
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

        const string backBranchName = nameof(ForwardFlowConflictResolutionTest);
        const string forwardBranchName = nameof(ForwardFlowConflictResolutionTest) + "-ff";

        // 1. Change file in VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / "1a.txt", "one");
        await GitOperations.CommitAll(VmrPath, "1a.txt");

        // 2. Open a backflow PR
        var codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the vmr and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "1b.txt", "one again");
        await GitOperations.CommitAll(VmrPath, "1b.txt");
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 3. Change file in the repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "3a.txt", "three");
        await GitOperations.CommitAll(ProductRepoPath, "3a.txt");

        // 4. Open a forward flow PR
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "3b.txt", "three again");
        await GitOperations.CommitAll(ProductRepoPath, "3b.txt");
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 5. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

        // 6. Merge the forward flow PR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // 7. Forward flow again so the VMR version of the file will flow back to the VMR
        // While the VMR accepted the content from the repo but it will get overriden by the VMR content again
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

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

        const string backBranchName = nameof(ForwardFlowConflictResolutionTest);
        const string forwardBranchName = nameof(ForwardFlowConflictResolutionTest) + "-ff";

        // 0. Prepare repo and VMR
        await ChangeRepoFileAndFlowIt("AAA", forwardBranchName + "-first");
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName + "-first");

        // 1. Change a different file in VMR
        await File.WriteAllTextAsync(_productRepoVmrPath / "different-file.txt", "XXX");
        await GitOperations.CommitAll(VmrPath, "different-file.txt");

        // 2. Open a backflow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        var codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 3-4. Change the file in the repo again
        codeFlowResult = await ChangeRepoFileAndFlowIt("BBB", forwardBranchName + "-second");
        codeFlowResult.ShouldHaveUpdates();

        // 5. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

        // 6. Merge the forward flow PR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName + "-second");

        // 7-8. Update the file again in the repo
        codeFlowResult = await ChangeRepoFileAndFlowIt("CCC", forwardBranchName + "-third");

        // 9. Merge the forward flow PR - any conflicts are dealt with automatically
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName + "-third");

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

        const string backBranchName = nameof(BackflowConflictWithPreviousFlowAutoResolutionTest);
        const string forwardBranchName = nameof(BackflowConflictWithPreviousFlowAutoResolutionTest) + "-ff";

        // 0. Prepare repo and VMR
        await ChangeVmrFileAndFlowIt("AAA", backBranchName + "-first");
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName + "-first");

        // 1. Change a different file in the repo
        await File.WriteAllTextAsync(ProductRepoPath / "different-file.txt", "XXX");
        await GitOperations.CommitAll(ProductRepoPath, "different-file.txt");

        // 2. Open a forwardflow PR
        await GitOperations.Checkout(VmrPath, "main");
        var codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 3-4. Change the file in the VMR again
        codeFlowResult = await ChangeVmrFileAndFlowIt("BBB", backBranchName + "-second");
        codeFlowResult.ShouldHaveUpdates();

        // 5. Merge the forwardflow PR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // 6. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName + "-second");

        // 7-8. Update the file again in the VMR
        codeFlowResult = await ChangeVmrFileAndFlowIt("CCC", backBranchName + "-third");

        // 9. Merge the backflow PR - any conflicts are dealt with automatically
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName + "-third");

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

        var backBranchName = GetTestBranchName(forwardFlow: false);
        var forwardBranchName = GetTestBranchName();
        Build build;
        CodeFlowResult codeFlowResult;

        // 0. Backflow of a build to populate the version files in the repo with some values
        build = await CreateNewVmrBuild([(FakePackageName, FakePackageVersion)]);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

        // 1. Change file in the repo
        await File.WriteAllTextAsync(ProductRepoPath / "1a.txt", "one");
        await GitOperations.CommitAll(ProductRepoPath, "1a.txt");

        // 2. Open a forward flow PR
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "1b.txt", "one again");
        await GitOperations.CommitAll(ProductRepoPath, "1b.txt");
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 3. Change file in the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "3a.txt", "three");
        await GitOperations.CommitAll(VmrPath, "3a.txt");

        // 4. Open a backflow PR
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.1")]);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "3b.txt", "three again");
        await GitOperations.CommitAll(VmrPath, "3b.txt");
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.2")]);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        // 5. Merge the forward flow PR
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // 6. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);
        var shaInStep6 = await GitOperations.GetRepoLastCommit(ProductRepoPath);

        // 7. Flow back again so the VMR version of the file will flow back to the repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.3")]);
        backBranchName = GetTestBranchName(forwardFlow: false);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

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
        forwardBranchName = GetTestBranchName();
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

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
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);
        build = await CreateNewVmrBuild([(FakePackageName, "1.0.5")]);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backBranchName, build);
        codeFlowResult.ShouldHaveUpdates();

        // 13. Merge the forward flow PR - any conflicts in version files are dealt with automatically
        // The conflict is described in the BackwardFlowConflictResolver class
        await GitOperations.MergePrBranch(ProductRepoPath, backBranchName);

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
        forwardBranchName = GetTestBranchName();
        await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

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
          │  2.                 │   
          │   O◄────────────────O 1.
          │   │            4.   │   
        3.O───┼────────────►O   │   
          │   │             │   │   
          │ x─┘ 5.       7. x   │   
          │ │               │   │   
        6.O◄┘               └──►O 8.
          │                     │   
          |────────────────────►O 9.
          │                     │   
     */
    [Test]
    public async Task OutOfOrderMergesWithConflictsTest()
    {
        await EnsureTestRepoIsInitialized();

        var aFileContent = "Added a new file in the repo";
        const string bFileContent = "Added a new file in the VMR";
        const string backBranchName = nameof(OutOfOrderMergesWithConflictsTest);
        const string forwardBranchName = nameof(OutOfOrderMergesWithConflictsTest) + "-ff";

        // Do a forward flow once and merge so we have something to fall back on
        var codeFlowResult = await ChangeRepoFileAndFlowIt("New content in the individual repo", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, forwardBranchName);

        // 1. Change file in VMR
        // 2. Open a backflow PR
        await File.WriteAllTextAsync(_productRepoVmrPath / "b.txt", bFileContent);
        await GitOperations.CommitAll(VmrPath, bFileContent);
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR #1", backBranchName);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeVmrFileAndFlowIt("New content from the VMR #2", backBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 3. Change file in the repo
        // 4. Open a forward flow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "a.txt", aFileContent);
        await GitOperations.CommitAll(ProductRepoPath, aFileContent);
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content from the individual repo #1", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        // We make another commit in the repo and add it to the PR branch (this is not in the diagram above)
        await GitOperations.Checkout(ProductRepoPath, "main");
        codeFlowResult = await ChangeRepoFileAndFlowIt("New content from the individual repo #2", forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();

        // 5. The backflow PR is now in conflict - repo has the content from step 3 but VMR has the one from step 1
        // 6. We resolve the conflict by using the content from the VMR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.VerifyMergeConflict(ProductRepoPath, backBranchName,
            mergeTheirs: true,
            expectedConflictingFile: _productRepoFileName);
        CheckFileContents(_productRepoFilePath, "New content from the VMR #2");

        // 7. The forward flow PR will have a conflict the opposite way - repo has the content from step 3 but VMR has the one from step 1
        // 8. We resolve the conflict by using the content from the VMR too
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.VerifyMergeConflict(VmrPath, forwardBranchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "New content from the individual repo #2");

        // 9. We try to forward flow again so the VMR version of the file will flow back to the VMR
        // While the VMR accepted the content from the repo but it will get overriden by the VMR content again
        await GitOperations.Checkout(ProductRepoPath, "main");
        await GitOperations.Checkout(VmrPath, "main");
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branch: forwardBranchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, forwardBranchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);

        // Both VMR and repo need to have the version from the VMR as it flowed to the repo and back
        CheckFileContents(_productRepoFilePath, "New content from the VMR #2");
        CheckFileContents(_productRepoVmrFilePath, "New content from the VMR #2");
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
        const string branchName = nameof(VersionDetailsConflictTest);

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
        await File.WriteAllTextAsync(ProductRepoPath / VersionFiles.VersionProps,
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
        var codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

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
        var vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps);

        // Update repo2 dependencies in the VMR
        vmrVersionDetails = vmrVersionDetails
            .Replace(@"Package.C2"" Version=""1.0.0""", @"Package.C2"" Version=""2.0.0""")
            .Replace("<Sha>c03</Sha>", "<Sha>c04</Sha>");

        vmrVersionProps = vmrVersionProps
            .Replace("PackageC2PackageVersion>1.0.0", "PackageC2PackageVersion>2.0.0");

        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionDetailsXml, vmrVersionDetails);
        await File.WriteAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps, vmrVersionProps);
        await GitOperations.CommitAll(VmrPath, "Update repo2 dependencies in the VMR");

        // Flow repo to the VMR
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "2");
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "2");

        // Flow changes back from the VMR
        var build = await CreateNewVmrBuild([("Package.A1", "1.0.20")]);
        codeFlowResult = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName + "3", build);
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(ProductRepoPath, branchName + "3");

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
        codeFlowResult = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName + "4");
        codeFlowResult.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName + "4");

        new VersionDetailsParser()
            .ParseVersionDetailsFile(_productRepoVmrPath / VersionFiles.VersionDetailsXml)
            .Dependencies
            .Should().BeEquivalentTo(expectedDependencies);

        vmrVersionProps = await File.ReadAllTextAsync(_productRepoVmrPath / VersionFiles.VersionProps);
        CheckFileContents(ProductRepoPath / VersionFiles.VersionProps, expected: vmrVersionProps);
    }
}

