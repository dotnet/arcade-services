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

#nullable enable
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
        var patchPathInRepo = _installerRepoPath / "patches" / "test-repo" / "example.patch";
        var patchPathInVmr = _vmrPath / "src" / "installer" / "patches" / "test-repo" / "example.patch";

        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit("installer", _installerRepoPath);
        await InitializeRepoAtLastCommit("test-repo", _privateRepoPath);

        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePath,
            patchPathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { "test-repo", "installer" },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, "test-file-after-patch.txt");
        await GitOperations.CheckAllIsCommited(_vmrPath);

        // a change in the patch

        //File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / "changed-patch.patch", _installerRepoPath / "patches" / "test-repo" / "example.patch", true);
        File.WriteAllText(patchPathInRepo, File.ReadAllText(patchPathInRepo).Replace("CCC", "DDD"));
        await GitOperations.CommitAll(_installerRepoPath, "Change the patch file");
        await UpdateRepoToLastCommit("installer", _installerRepoPath);

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, "test-file-after-changed-patch.txt");

        // remove the patch from installer

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit("installer", _installerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, "test-repo-file.txt");

        // add a new patch in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / "new-patch.patch", _installerRepoPath / "patches" / "test-repo" / "new-patch.patch");
        await GitOperations.CommitAll(_installerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit("installer", _installerRepoPath);

        expectedFiles.Add(_vmrPath / "src" / "installer" / "patches" / "test-repo" / "new-patch.patch");
        CheckDirectoryContents(_vmrPath, expectedFiles);
        CompareFileContents(testRepoFilePath, "test-file-after-new-patch.txt");
     
        // change the file so the vmr patch cannot be applied

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / "changed-test-repo-file.txt", _privateRepoPath / "test-repo-file.txt", true);
        await GitOperations.CommitAll(_privateRepoPath, "change file in private repo");
        var commit = await GitOperations.GetRepoLastCommit(_privateRepoPath);
        this.Invoking(x => x.CallDarcUpdate("test-repo", commit)).Should().Throw<Exception>();

        // res = await CallDarcUpdate("test-repo", commit);
        //res.ExitCode.Should().NotBe(0, res.StandardOutput);
    }
}
