// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.VirtualMonoRepo.E2E.Tests;

[TestFixture]
internal class VmrPatchRemovingFileTest : VmrPatchesTestsBase
{
    public VmrPatchRemovingFileTest() : base("remove-file.patch")
    {
    }

    [Test]
    public async Task VmrPatchAddsFileTest()
    {
        var patchPathInRepo = InstallerPatchesDir / PatchFileName;

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<NativePath>
        {
            VmrPatchesDir / PatchFileName,
            InstallerFilePathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName, Constants.InstallerRepoName],
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(ProductRepoFilePathInVmr);
        expectedFiles.Remove(VmrPatchesDir / PatchFileName);

        CheckDirectoryContents(VmrPath, expectedFiles);
    }
}

