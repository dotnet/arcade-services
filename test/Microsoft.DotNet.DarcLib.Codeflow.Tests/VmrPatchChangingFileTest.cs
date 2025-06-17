// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class VmrPatchChangingFileTest : VmrPatchesTestsBase
{
    public VmrPatchChangingFileTest() : base("example.patch")
    {
    }

    [Test]
    public async Task PatchesAreAppliedTest()
    {
        var patchPathInVmr = VmrPatchesDir / PatchFileName;
        var productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);

        // Initialize repo with a VMR patch (example.patch which changes BBB->CCC)

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<NativePath>
        {
            ProductRepoFilePathInVmr,
            InstallerFilePathInVmr,
            patchPathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName, Constants.InstallerRepoName],
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, "test-file-after-patch.txt");
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // We change the patch to BBB->DDD

        await File.WriteAllTextAsync(
            InstallerPatchesDir / PatchFileName,
            await File.ReadAllTextAsync(VmrTestsOneTimeSetUp.ResourcesPath / "changed-patch.patch"));
        await GitOperations.CommitAll(InstallerRepoPath, "Change the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, "test-file-after-changed-patch.txt");

        // Remove the patch from installer, file goes back to BBB

        File.Delete(InstallerPatchesDir / PatchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, productRepoFileName);

        // Add a new patch in installer which changes AAA->TTT (file should have BBB->TTT)

        var newPatchFileName = "new-patch.patch";
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / newPatchFileName, InstallerPatchesDir / newPatchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(VmrPatchesDir / newPatchFileName);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, "test-file-after-new-patch.txt");

        // Change the file so the VMR patch cannot be applied

        await File.WriteAllTextAsync(ProductRepoPath / productRepoFileName, "New content");
        await GitOperations.CommitAll(ProductRepoPath, "Change file in product repo");
        var commit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        await this.Awaiting(_ => CallDarcUpdate(Constants.ProductRepoName, commit)).Should().ThrowAsync<Exception>();
    }
}
