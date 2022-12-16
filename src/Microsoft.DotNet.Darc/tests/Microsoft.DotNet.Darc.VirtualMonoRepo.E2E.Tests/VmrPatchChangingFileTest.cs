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
        var fileAfterPatch = "test-file-after-patch.txt";
        var fileAfterChangedPatch = "test-file-after-changed-patch.txt";
        var newPatchFileName = "new-patch.patch";
        var fileAfterNewPatchName = "test-file-after-new-patch.txt";
        var changedFileName = "changed-test-repo-file.txt";


        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, _installerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var testRepoFilePath = _vmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / Constants.ProductRepoFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            patchPathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterPatch);
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // a change in the patch

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / "changed-patch.patch", installerPatchesDir / patchFileName, true);
        await GitOperations.CommitAll(_installerRepoPath, "Change the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterChangedPatch);

        // remove the patch from installer

        File.Delete(installerPatchesDir / patchFileName);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, Constants.ProductRepoFileName);

        // add a new patch in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / newPatchFileName, installerPatchesDir / newPatchFileName);
        await GitOperations.CommitAll(_installerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Add(vmrPatchesDir / newPatchFileName);
        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, fileAfterNewPatchName);
     
        // change the file so the vmr patch cannot be applied

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / changedFileName, _privateRepoPath / Constants.ProductRepoFileName, true);
        await GitOperations.CommitAll(_privateRepoPath, "Change file in product repo");
        var commit = await GitOperations.GetRepoLastCommit(_privateRepoPath);
        this.Invoking(x => x.CallDarcUpdate(Constants.ProductRepoName, commit)).Should().Throw<Exception>();
    }
}
