// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        var vmrSourcesPath = _vmrPath / VmrInfo.SourcesDir;
        var patchPathInRepo = installerPatchesDir / patchFileName;

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, _installerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var testRepoFilePath = vmrSourcesPath / Constants.ProductRepoName / Constants.ProductRepoFileName;
        var newFilePath = vmrSourcesPath / Constants.ProductRepoName / _productRepoNewFile;
        var patchPath = vmrSourcesPath / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            installerFilePath,
            newFilePath,
            patchPath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(_vmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Remove(newFilePath);
        expectedFiles.Remove(patchPath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
    }
}
