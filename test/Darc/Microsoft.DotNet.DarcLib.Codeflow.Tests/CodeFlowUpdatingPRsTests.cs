// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class CodeFlowUpdatingPRsTests : CodeFlowTests
{
    // This test simulates the following scenario:
    //   1. We open a backflow PR.
    //   2. We open a FF PR.
    //   3. We make some changes to the FF PR.
    //   4. We merge the backflow PR — the order of steps 3 and 4 doesn’t matter.
    //   5. Then, another build updates the FF PR, overwriting the changes we made earlier.
    [Test]
    public async Task ForwardFlowWithChangesInThePrBranchTest()
    {
        await EnsureTestRepoIsInitialized();

        const string backflowBranch = nameof(ForwardFlowWithChangesInThePrBranchTest) + "-bf";
        const string forwardFlowBranch = nameof(ForwardFlowWithChangesInThePrBranchTest) + "-ff";

        // Make changes in the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "repo.txt", "New file in the repo");
        await GitOperations.CommitAll(ProductRepoPath, "New file in the repo");

        // Make changes in the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "vmr.txt", "New file in the VMR");
        await GitOperations.CommitAll(VmrPath, "New file in the VMR");

        // 1. Open a backflow PR
        var result = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "Backflow commit");

        // 2. Open a forward flow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        result = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "Forward flow commit");

        // 3. Make some changes in the forward flow PR branch
        await GitOperations.Checkout(VmrPath, forwardFlowBranch);
        await File.WriteAllTextAsync(_productRepoVmrPath / "repo.txt", "Updated file in the PR");
        await GitOperations.CommitAll(VmrPath, "Updated file in the PR");

        // 4. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backflowBranch);

        // 5. Flow the changes from the repo into the VMR PR
        result = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "Forward flow update", allowEmpty: true);

        // Check that the changes in the PR branch are preserved
        var prFileContent = await File.ReadAllTextAsync(_productRepoVmrPath / "repo.txt");
        prFileContent.Should().Be("Updated file in the PR", "The changes in the PR branch should be preserved after the backflow merge.");

        // Check that the changes from the repo are also present
        var vmrFileContent = await File.ReadAllTextAsync(_productRepoVmrPath / "vmr.txt");
        vmrFileContent.Should().Be("New file in the VMR", "The changes from the repo should be present in the VMR after the backflow merge.");
    }

    // Mirrored version of the above test
    [Test]
    public async Task BackflowWithChangesInThePrBranchTest()
    {
        await EnsureTestRepoIsInitialized();

        const string backflowBranch = nameof(BackflowWithChangesInThePrBranchTest) + "-bf";
        const string forwardFlowBranch = nameof(BackflowWithChangesInThePrBranchTest) + "-ff";

        // Make changes in the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "repo.txt", "New file in the repo");
        await GitOperations.CommitAll(ProductRepoPath, "New file in the repo");

        // Make changes in the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / "vmr.txt", "New file in the VMR");
        await GitOperations.CommitAll(VmrPath, "New file in the VMR");

        // 1. Open a forward flow PR
        var result = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(VmrPath, "Forward flow commit");

        // 2. Open a backflow PR
        await GitOperations.Checkout(VmrPath, "main");
        result = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "Backflow commit");

        // 3. Make some changes in the backflow PR branch
        await GitOperations.Checkout(ProductRepoPath, backflowBranch);
        await File.WriteAllTextAsync(ProductRepoPath / "vmr.txt", "Updated file in the PR");
        await GitOperations.CommitAll(ProductRepoPath, "Updated file in the PR");

        // 4. Make some additional change in the forward flow so that we have something to flow later and merge it
        await GitOperations.Checkout(VmrPath, forwardFlowBranch);
        await File.WriteAllTextAsync(_productRepoVmrPath / "vmr2.txt", "Another new file in the PR");
        await GitOperations.CommitAll(VmrPath, "Updated file in the VMR");
        await GitOperations.MergePrBranch(VmrPath, forwardFlowBranch);

        // 5. Flow the changes from the VMR into the product repo PR
        result = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.CommitAll(ProductRepoPath, "Backflow update", allowEmpty: true);

        // Check that the changes in the PR branch are preserved
        var prFileContent = await File.ReadAllTextAsync(ProductRepoPath / "vmr.txt");
        prFileContent.Should().Be("Updated file in the PR", "The changes in the PR branch should be preserved after the forward flow merge.");
        prFileContent = await File.ReadAllTextAsync(ProductRepoPath / "vmr2.txt");
        prFileContent.Should().Be("Another new file in the PR");

        // Check that the changes from the VMR are also present
        var repoFileContent = await File.ReadAllTextAsync(ProductRepoPath / "repo.txt");
        repoFileContent.Should().Be("New file in the repo", "The changes from the VMR should be present in the repo after the forward flow merge.");
    }

    // This test simulates the following scenario:
    //   1. A change is made directly in the VMR, touching file Foo in repo A
    //   2. A number of forward flows come from repo A but they're not touching the file Foo
    //   3. Finally, Foo is changed in the repo A, and we try to open a FF PR.
    //   4. This fails because there's a conflict in file Foo, so we recreate the last FF and try to apply the diff again,
    //      but it fails again, because the last FF isn't old enough.
    //      This test tests that we can go deeper in the past and recreate the flows correctly.
    [Test]
    public async Task ForwardFlowWithConflictsDeepInPastTest()
    {
        await EnsureTestRepoIsInitialized();

        string forwardFlowBranch = GetTestBranchName(forwardFlow: true);

        const string conflictingFileName = "Foo.txt";

        // 1. Create the Foo changes in the VMR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / conflictingFileName, "A new file in the VMR");
        await GitOperations.CommitAll(VmrPath, "A new file in the VMR");

        // 2. Make a number of forward flows
        await ChangeRepoFileAndFlowIt("Change one", forwardFlowBranch);
        await FinalizeForwardFlow(forwardFlowBranch);

        await ChangeRepoFileAndFlowIt("Change two", forwardFlowBranch);
        await FinalizeForwardFlow(forwardFlowBranch);

        await ChangeRepoFileAndFlowIt("Change three", forwardFlowBranch);
        await FinalizeForwardFlow(forwardFlowBranch);

        // 3. Foo is changed in the repo A, and we try to open a FF PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / conflictingFileName, "Foo is changed in the repo");
        await GitOperations.CommitAll(ProductRepoPath, "Foo is changed in the repo");

        // 4. Try to open a forward flow PR
        var result = await CallForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        var conflictedFile = VmrInfo.GetRelativeRepoSourcesPath(Constants.ProductRepoName) / conflictingFileName;
        // Verify that there is a conflict in Foo.txt
        await GitOperations.VerifyMergeConflict(
            VmrPath,
            forwardFlowBranch,
            [conflictedFile],
            mergeTheirs: true);

        var content = await File.ReadAllTextAsync(VmrPath / conflictedFile);
        content.Should().Be("Foo is changed in the repo");
        content = await File.ReadAllTextAsync(_productRepoVmrFilePath);
        content.Should().Be("Change three");
    }

    // This test simulates the following scenario:
    //   1. A change is made directly in the product repo, touching file Foo
    //   2. A number of backflows come from the VMR but they're not touching the file Foo
    //   3. Finally, Foo is changed in the VMR, and we try to open a backflow PR.
    //   4. This fails because there's a conflict in file Foo, so we recreate the last backflow and try to apply the diff again,
    //      but it fails again, because the last backflow isn't old enough.
    //      This test tests that we can go deeper in the past and recreate the flows correctly.
    [Test]
    public async Task BackflowWithConflictsDeepInPastTest()
    {
        await EnsureTestRepoIsInitialized();

        string backflowBranch = GetTestBranchName();
        const string conflictingFileName = "Foo.txt";

        // 0. Make a baseline backflow as the pre-prepared repos don't have one
        await ChangeVmrFileAndFlowIt("Baseline change", backflowBranch);
        await FinalizeBackFlow(backflowBranch);

        // 1. Create the Foo changes in the product repo
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / conflictingFileName, "A new file in the product repo");
        await GitOperations.CommitAll(ProductRepoPath, "A new file in the product repo");

        // 2. Make a number of backflows
        await ChangeVmrFileAndFlowIt("Change one", backflowBranch);
        await FinalizeBackFlow(backflowBranch);

        await ChangeVmrFileAndFlowIt("Change two", backflowBranch);
        await FinalizeBackFlow(backflowBranch);

        await ChangeVmrFileAndFlowIt("Change three", backflowBranch);
        await FinalizeBackFlow(backflowBranch);

        // 3. Foo is changed in the VMR, and we try to open a backflow PR
        await GitOperations.Checkout(VmrPath, "main");
        await File.WriteAllTextAsync(_productRepoVmrPath / conflictingFileName, "Foo is changed in the VMR");
        await GitOperations.CommitAll(VmrPath, "Foo is changed in the VMR");

        // 4. Try to open a backflow PR
        var result = await CallBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        await GitOperations.VerifyMergeConflict(
            ProductRepoPath,
            backflowBranch,
            [conflictingFileName],
            mergeTheirs: true);

        var content = await File.ReadAllTextAsync(ProductRepoPath / conflictingFileName);
        content.Should().Be("Foo is changed in the VMR");
        content = await File.ReadAllTextAsync(_productRepoFilePath);
        content.Should().Be("Change three");
    }
}

