// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

[TestFixture]
internal class VmrForwardFlowTest : VmrCodeFlowTests
{
    [Test]
    public async Task OnlyForwardflowsTest()
    {
        await EnsureTestRepoIsInitialized();

        const string branchName = nameof(OnlyForwardflowsTest);

        var hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Flow again - should be a no-op
        hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        hadUpdates.ShouldNotHaveUpdates();
        await GitOperations.Checkout(VmrPath, "main");
        await GitOperations.DeleteBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo");

        // Make a change in the repo again
        hadUpdates = await ChangeRepoFileAndFlowIt("New content in the individual repo again", branchName);
        hadUpdates.ShouldHaveUpdates();
        CheckFileContents(_productRepoVmrFilePath, "New content in the individual repo again");

        // Make an additional change in the PR branch before merging
        await File.WriteAllTextAsync(_productRepoVmrFilePath, "Change that happened in the PR");
        await GitOperations.CommitAll(VmrPath, "Extra commit in the PR");
        await GitOperations.MergePrBranch(VmrPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "Change that happened in the PR");

        // Make a conflicting change in the VMR
        hadUpdates = await ChangeRepoFileAndFlowIt("A completely different change", branchName);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.VerifyMergeConflict(VmrPath, branchName,
            mergeTheirs: true,
            expectedConflictingFile: VmrInfo.SourcesDir / Constants.ProductRepoName / _productRepoFileName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");

        // We used the changes from the repo - let's verify flowing back won't change anything
        hadUpdates = await CallDarcBackflow(Constants.ProductRepoName, ProductRepoPath, branchName);
        CheckFileContents(_productRepoVmrFilePath, "A completely different change");
    }

    [Test]
    public async Task ForwardFlowingDependenciesTest()
    {
        const string branchName = nameof(ForwardFlowingDependenciesTest);

        await EnsureTestRepoIsInitialized();

        var vmrSha = await GitOperations.GetRepoLastCommit(VmrPath);

        await GetLocal(VmrPath).AddDependencyAsync(new DependencyDetail
        {
            Name = "Package.A1",
            Version = "1.0.0",
            RepoUri = ProductRepoPath,
            Commit = "123abc",
            Type = DependencyType.Product,
            Pinned = false,
        });
        await GitOperations.CommitAll(VmrPath, "Added Package.A1 dependency");

        // Flow a build into the VMR
        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(_productRepoFilePath, "New content in the repository");
        await GitOperations.CommitAll(ProductRepoPath, "Changing a repo file");

        var build = await CreateNewRepoBuild(
        [
            ("Package.A1", "1.0.1"),
        ]);

        var hadUpdates = await CallDarcForwardflow(Constants.ProductRepoName, ProductRepoPath, branchName, buildToFlow: build.Id);
        hadUpdates.ShouldHaveUpdates();
        await GitOperations.MergePrBranch(VmrPath, branchName);

        // Verify that VMR's version files have the new versions
        var vmrVersionDetails = new VersionDetailsParser()
            .ParseVersionDetailsFile(VmrPath / VersionFiles.VersionDetailsXml);

        vmrVersionDetails.Dependencies.Where(d => d.Name != DependencyFileManager.ArcadeSdkPackageName)
            .Should().BeEquivalentTo(GetDependencies(build));

        var propName = VersionFiles.GetVersionPropsPackageVersionElementName("Package.A1");
        var vmrVersionProps = AllVersionsPropsFile.DeserializeFromXml(VmrPath / VersionFiles.VersionProps);
        vmrVersionProps.Versions[propName].Should().Be("1.0.1");
    }
}

