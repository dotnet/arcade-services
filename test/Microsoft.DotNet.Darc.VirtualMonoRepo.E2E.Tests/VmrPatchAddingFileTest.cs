// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrPatchAddingFileTest : VmrPatchesTestsBase
{
    private readonly string _productRepoNewFile = "new-file.txt";

    public VmrPatchAddingFileTest() : base("add-file.patch")
    {
    }

    [Test]
    public async Task VmrPatchAddsFileTest()
    {
        var vmrSourcesPath = VmrPath / VmrInfo.SourcesDir;
        var patchPathInRepo = InstallerPatchesDir / PatchFileName;

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var newFilePath = vmrSourcesPath / Constants.ProductRepoName / _productRepoNewFile;
        var patchPath = VmrPatchesDir / PatchFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            ProductRepoFilePathInVmr,
            InstallerFilePathInVmr,
            newFilePath,
            patchPath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Remove(newFilePath);
        expectedFiles.Remove(patchPath);

        CheckDirectoryContents(VmrPath, expectedFiles);
    }
}
