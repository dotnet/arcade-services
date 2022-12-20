// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrPatchAddingSubmoduleFileTest : VmrPatchesTestsBase
{
    public VmrPatchAddingSubmoduleFileTest() : base("add-submodule-file.patch")
    {
    }
    protected override async Task CopyReposForCurrentTest()
    {
        await base.CopyReposForCurrentTest();
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.SubmoduleRepoName);
    }

    [Test]
    public async Task PatchesAreAppliedTest()
    {
        var vmrSourcesPath = _vmrPath / VmrInfo.SourcesDir;
        const string FileCreatedByPatch = "patched-submodule-file.txt";

        var testRepoPathInVmr = vmrSourcesPath / Constants.ProductRepoName;
        var patchPathInRepo = _installerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName;
        var patchPathInVmr = vmrSourcesPath / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName;
        var installerFilePathInVmr = vmrSourcesPath / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var submoduleRelativePath = new NativePath("submodules") / "submodule1";
        var submodulePathInVmr = testRepoPathInVmr / "submodules" / "submodule1";
        var submodulePathInRepo = "foo";
        var patchedSubmoduleFileInVmr = submodulePathInVmr / submodulePathInRepo / FileCreatedByPatch;

        await GitOperations.InitializeSubmodule(_privateRepoPath, "submodule1", _externalRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(_privateRepoPath, "Added a submodule");

        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, _installerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, _privateRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoPathInVmr / Constants.ProductRepoFileName,
            submodulePathInVmr / "external-repo-file.txt",
            submodulePathInVmr / VersionFiles.VersionDetailsXml,
            installerFilePathInVmr,
            patchPathInVmr,
            patchedSubmoduleFileInVmr,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            _vmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(patchedSubmoduleFileInVmr, "new file");

        // Remove a patch that added a submodule file

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        expectedFiles.Remove(patchedSubmoduleFileInVmr);
        CheckDirectoryContents(_vmrPath, expectedFiles);
        File.Exists(patchedSubmoduleFileInVmr).Should().BeFalse();

        // Add the patch back in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, patchPathInRepo);
        await GitOperations.CommitAll(_installerRepoPath, "Add the patch back");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, _installerRepoPath);

        expectedFiles.Add(patchPathInVmr);
        expectedFiles.Add(patchedSubmoduleFileInVmr);
        CheckDirectoryContents(_vmrPath, expectedFiles);
        CheckFileContents(patchedSubmoduleFileInVmr, "new file");

        // Add the file to the submodule so the vmr patch cannot be applied

        Directory.CreateDirectory(_externalRepoPath / submodulePathInRepo);
        File.WriteAllText(_externalRepoPath / submodulePathInRepo / FileCreatedByPatch, "New content");
        await GitOperations.CommitAll(_externalRepoPath, "Added a new file into the repo");

        // Move submodule to a new commit and verify it breaks the patch

        await GitOperations.UpdateSubmodule(_privateRepoPath, submodulePathInRepo);
        var commit = await GitOperations.GetRepoLastCommit(_privateRepoPath);
        this.Awaiting(_ => CallDarcUpdate(Constants.ProductRepoName, commit)).Should().Throw<Exception>();
    }
}
