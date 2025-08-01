// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
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
        var result = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        // 2. Open a forward flow PR
        await GitOperations.Checkout(ProductRepoPath, "main");
        result = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        // 3. Make some changes in the forward flow PR branch
        await GitOperations.Checkout(VmrPath, forwardFlowBranch);
        await File.WriteAllTextAsync(_productRepoVmrPath / "repo.txt", "Updated file in the PR");
        await GitOperations.CommitAll(VmrPath, "Updated file in the PR");

        // 4. Merge the backflow PR
        await GitOperations.MergePrBranch(ProductRepoPath, backflowBranch);

        // 5. Flow the changes from the repo into the VMR PR
        result = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

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
        var result = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, forwardFlowBranch);
        result.ShouldHaveUpdates();

        // 2. Open a backflow PR
        await GitOperations.Checkout(VmrPath, "main");
        result = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

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
        result = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, backflowBranch);
        result.ShouldHaveUpdates();

        // Check that the changes in the PR branch are preserved
        var prFileContent = await File.ReadAllTextAsync(ProductRepoPath / "vmr.txt");
        prFileContent.Should().Be("Updated file in the PR", "The changes in the PR branch should be preserved after the forward flow merge.");
        prFileContent = await File.ReadAllTextAsync(ProductRepoPath / "vmr2.txt");
        prFileContent.Should().Be("Another new file in the PR");

        // Check that the changes from the VMR are also present
        var repoFileContent = await File.ReadAllTextAsync(ProductRepoPath / "repo.txt");
        repoFileContent.Should().Be("New file in the repo", "The changes from the VMR should be present in the repo after the forward flow merge.");
    }
}

