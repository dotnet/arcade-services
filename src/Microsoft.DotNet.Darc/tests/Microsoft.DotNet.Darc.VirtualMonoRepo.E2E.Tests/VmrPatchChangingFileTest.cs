// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrPatchChangingFileTest : VmrPatchesTestsBase
{
    public VmrPatchChangingFileTest() : base("example.patch")
    {
    }

    [Test]
    public async Task PatchesAreAppliedTest()
    {
        var patchPathInVmr = VmrPatchesDir / PatchFileName;
        var productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
        var fileAfterPatch = "test-file-after-patch.txt";
        var fileAfterChangedPatch = "test-file-after-changed-patch.txt";
        var newPatchFileName = "new-patch.patch";
        var fileAfterNewPatchName = "test-file-after-new-patch.txt";
        var changedFileName = "changed-test-repo-file.txt";

        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            ProductRepoFilePathInVmr,
            InstallerFilePathInVmr,
            patchPathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, fileAfterPatch);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // a change in the patch

        File.WriteAllText(
            InstallerPatchesDir / PatchFileName,
            File.ReadAllText(VmrTestsOneTimeSetUp.ResourcesPath / "changed-patch.patch"));
        await GitOperations.CommitAll(InstallerRepoPath, "Change the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, fileAfterChangedPatch);

        // remove the patch from installer

        File.Delete(InstallerPatchesDir / PatchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, productRepoFileName);

        // add a new patch in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / newPatchFileName, InstallerPatchesDir / newPatchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(VmrPatchesDir / newPatchFileName);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(ProductRepoFilePathInVmr, fileAfterNewPatchName);

        // change the file so the vmr patch cannot be applied

        var before = File.ReadAllText(ProductRepoPath / productRepoFileName);
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / changedFileName, ProductRepoPath / productRepoFileName, true);
        var after = File.ReadAllText(ProductRepoPath / productRepoFileName);

        throw new Exception(
            $"File {ProductRepoPath / productRepoFileName} before:" +
            $"\r\n{before}" +
            $"\r\n\r\n\r\nFile after:" +
            $"\r\n{after}");

        //await GitOperations.CommitAll(ProductRepoPath, "Change file in product repo");
        //var commit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        //this.Awaiting(_ => CallDarcUpdate(Constants.ProductRepoName, commit)).Should().Throw<Exception>();
    }
}
