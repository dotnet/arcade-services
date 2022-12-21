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
public class VmrPatchRemovingFileTest : VmrPatchesTestsBase
{
    public VmrPatchRemovingFileTest() : base("remove-file.patch")
    {
    }

    [Test]
    public async Task VmrPatchAddsFileTest()
    {
        var productRepoFileName = Constants.GetRepoFileName(Constants.ProductRepoName);
        var patchPathInRepo = installerPatchesDir / patchFileName;

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var testRepoFilePath = VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / productRepoFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            vmrPatchesDir / patchFileName,
            installerFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(VmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(testRepoFilePath);
        expectedFiles.Remove(vmrPatchesDir / patchFileName);

        CheckDirectoryContents(VmrPath, expectedFiles);
    }
}

