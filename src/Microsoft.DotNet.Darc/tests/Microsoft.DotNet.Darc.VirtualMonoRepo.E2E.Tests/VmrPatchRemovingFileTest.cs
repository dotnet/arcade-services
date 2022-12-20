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
        var patchPathInRepo = installerPatchesDir / patchFileName;

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, _installerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var testRepoFilePath = _vmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / Constants.ProductRepoFileName;

        var expectedFilesFromRepos = new List<LocalPath>
        {
            vmrPatchesDir / patchFileName,
            installerFilePath
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(_vmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Add(testRepoFilePath);
        expectedFiles.Remove(vmrPatchesDir / patchFileName);

        CheckDirectoryContents(_vmrPath, expectedFiles);
    }
}

