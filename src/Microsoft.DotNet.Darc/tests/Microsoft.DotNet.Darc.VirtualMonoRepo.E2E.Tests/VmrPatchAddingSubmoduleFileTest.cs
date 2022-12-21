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
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.SecondRepoName);
    }

    [Test]
    public async Task PatchesAreAppliedTest()
    {
        var vmrSourcesPath = VmrPath / VmrInfo.SourcesDir;
        const string FileCreatedByPatch = "patched-submodule-file.txt";

        var testRepoPathInVmr = vmrSourcesPath / Constants.ProductRepoName;
        var patchPathInRepo = InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName;
        var patchPathInVmr = vmrSourcesPath / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName;
        var installerFilePathInVmr = vmrSourcesPath / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var testRepoFilePathInVmr = vmrSourcesPath / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName);
        var submoduleRelativePath = new NativePath("submodules") / "submodule1";
        var submodulePathInVmr = testRepoPathInVmr / "submodules" / "submodule1";
        var submodulePathInRepo = "foo";
        var patchedSubmoduleFileInVmr = submodulePathInVmr / submodulePathInRepo / FileCreatedByPatch;
        var submoduleFileInVmr = submodulePathInVmr / Constants.GetRepoFileName(Constants.SecondRepoName);

        await GitOperations.InitializeSubmodule(ProductRepoPath, "submodule1", SecondRepoPath, submoduleRelativePath);
        await GitOperations.CommitAll(ProductRepoPath, "Added a submodule");

        // initialize repo with a vmr patch

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            testRepoFilePathInVmr,
            submoduleFileInVmr,
            submodulePathInVmr / VersionFiles.VersionDetailsXml,
            installerFilePathInVmr,
            patchPathInVmr,
            patchedSubmoduleFileInVmr,
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName, Constants.InstallerRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(patchedSubmoduleFileInVmr, "new file");

        // Remove a patch that added a submodule file

        File.Delete(patchPathInRepo);
        await GitOperations.CommitAll(InstallerRepoPath, "Remove the patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Remove(patchPathInVmr);
        expectedFiles.Remove(patchedSubmoduleFileInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        File.Exists(patchedSubmoduleFileInVmr).Should().BeFalse();

        // Add the patch back in installer

        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, patchPathInRepo);
        await GitOperations.CommitAll(InstallerRepoPath, "Add the patch back");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(patchPathInVmr);
        expectedFiles.Add(patchedSubmoduleFileInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(patchedSubmoduleFileInVmr, "new file");

        // Add the file to the submodule so the vmr patch cannot be applied

        Directory.CreateDirectory(SecondRepoPath / submodulePathInRepo);
        File.WriteAllText(SecondRepoPath / submodulePathInRepo / FileCreatedByPatch, "New content");
        await GitOperations.CommitAll(SecondRepoPath, "Added a new file into the repo");

        // Move submodule to a new commit and verify it breaks the patch

        await GitOperations.UpdateSubmodule(ProductRepoPath, submodulePathInRepo);
        var commit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        this.Awaiting(_ => CallDarcUpdate(Constants.ProductRepoName, commit)).Should().Throw<Exception>();
    }
}
