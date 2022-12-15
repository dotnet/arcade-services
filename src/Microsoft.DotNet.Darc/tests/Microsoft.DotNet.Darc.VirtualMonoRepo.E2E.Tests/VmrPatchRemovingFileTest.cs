// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

#nullable enable
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
        var patchPathInRepo = _installerRepoPath / "patches" / "test-repo" / patchFileName;

        await InitializeRepoAtLastCommit(installerRepoName, _installerRepoPath);
        await InitializeRepoAtLastCommit(Constants.TestRepoName, _privateRepoPath);

        var testRepoFilePath = _vmrPath / "src" / "test-repo" / "test-repo-file.txt";

        var expectedFilesFromRepos = new List<LocalPath>
        {
            _vmrPath / "src" / "installer" / "patches" / "test-repo" / patchFileName,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.TestRepoName, installerRepoName },
            expectedFilesFromRepos);

        CheckDirectoryContents(_vmrPath, expectedFiles);

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit("installer", _installerRepoPath);

        expectedFiles.Add(testRepoFilePath);
        expectedFiles.Remove(_vmrPath / "src" / "installer" / "patches" / "test-repo" / patchFileName);

        CheckDirectoryContents(_vmrPath, expectedFiles);
    }
}

