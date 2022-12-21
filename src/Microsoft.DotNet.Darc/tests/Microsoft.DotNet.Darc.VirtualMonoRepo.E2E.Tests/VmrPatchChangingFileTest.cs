// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
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
        var patchPathInVmr = vmrPatchesDir / patchFileName;
        var productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
        var fileAfterPatch = "test-file-after-patch.txt";
        var fileAfterChangedPatch = "test-file-after-changed-patch.txt";
        var newPatchFileName = "new-patch.patch";
        var fileAfterNewPatchName = "test-file-after-new-patch.txt";
        var changedFileName = "changed-test-repo-file.txt";


        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var testRepoFilePath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / productRepoFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            installerFilePath,
            patchPathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterPatch);
        await GitOperations.CheckAllIsCommited(VmrPath);

        // a change in the patch

        File.WriteAllText(
            installerPatchesDir / patchFileName,
            File.ReadAllText(VmrTestsOneTimeSetUp.ResourcesPath / "changed-patch.patch"));
        await GitOperations.CommitAll(InstallerRepoPath, "Change the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterChangedPatch);

        // remove the patch from installer

        File.Delete(installerPatchesDir / patchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, productRepoFileName);

        // add a new patch in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / newPatchFileName, installerPatchesDir / newPatchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(vmrPatchesDir / newPatchFileName);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterNewPatchName);
     
        // change the file so the vmr patch cannot be applied

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / changedFileName, ProductRepoPath / productRepoFileName, true);
        await GitOperations.CommitAll(ProductRepoPath, "Change file in product repo");
        var commit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        this.Awaiting(_ => CallDarcUpdate(Constants.ProductRepoName, commit)).Should().Throw<Exception>();
    }
}
