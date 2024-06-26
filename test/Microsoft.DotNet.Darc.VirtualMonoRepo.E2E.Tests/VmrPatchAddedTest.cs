// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
internal class VmrPatchAddedTest : VmrTestsBase
{
    [Test]
    public async Task PatchesAreAppliedTest()
    {
        var newPatchFileName = "new-patch.patch";
        var vmrPatchesDir = VmrPath / VmrInfo.RelativeSourcesDir / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName;
        var patchPathInVmr = vmrPatchesDir / newPatchFileName;
        var installerPatchesDir = InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName;
        var installerFilePathInVmr = VmrPath / VmrInfo.RelativeSourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        var productRepoFilePathInVmr = VmrPath / VmrInfo.RelativeSourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName);

        await File.WriteAllTextAsync(ProductRepoPath / Constants.GetRepoFileName(Constants.ProductRepoName),
            """
            File in the test repo
            patches will change the next lines
            AAA
            CCC
            end of changes
            """);
        await GitOperations.CommitAll(ProductRepoPath, "Change file to CCC");

        // Update dependent repo to the last commit
        var productRepoSha = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        var productRepoDependency = string.Format(
            Constants.DependencyTemplate,
            Constants.ProductRepoName, ProductRepoPath, productRepoSha);

        var versionDetails = string.Format(
            Constants.VersionDetailsTemplate,
            productRepoDependency);

        await File.WriteAllTextAsync(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);
        await GitOperations.CommitAll(InstallerRepoPath, "Bump product repo to latest");

        await InitializeRepoAtLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        CheckFileContents(productRepoFilePathInVmr, "AAA", "CCC");

        await File.WriteAllTextAsync(ProductRepoPath / Constants.GetRepoFileName(Constants.ProductRepoName),
            """
            File in the test repo
            patches will change the next lines
            AAA
            BBB
            end of changes
            """);
        await GitOperations.CommitAll(ProductRepoPath, "Change file to BBB");

        var expectedFilesFromRepos = new List<NativePath>
        {
            productRepoFilePathInVmr,
            installerFilePathInVmr
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            [Constants.ProductRepoName, Constants.InstallerRepoName],
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);

        // Add a new patch in installer
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / newPatchFileName, installerPatchesDir / newPatchFileName);

        // Update dependent repo to the last commit
        productRepoSha = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        productRepoDependency = string.Format(
            Constants.DependencyTemplate,
            Constants.ProductRepoName, ProductRepoPath, productRepoSha);

        versionDetails = string.Format(
            Constants.VersionDetailsTemplate,
            productRepoDependency);

        File.WriteAllText(InstallerRepoPath / VersionFiles.VersionDetailsXml, versionDetails);

        await GitOperations.CommitAll(InstallerRepoPath, "Add a new patch file");
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        // Now we sync installer which means new patch + change in the product repo
        // We must check that the patch is detected as being added and won't be restored during repo's sync
        // The file will have AAA CCC in the beginning
        // The repo change will change it to AAA BBB
        // Then the patch will change it to TTT BBB
        // If we tried to restore the patch before we sync the repo, the patch fails
        // because it will find AAA CCC instead of AAA BBB (which it expects)
        await UpdateRepoToLastCommit(Constants.InstallerRepoName, InstallerRepoPath);

        expectedFiles.Add(patchPathInVmr);
        CheckDirectoryContents(VmrPath, expectedFiles);
        CheckFileContents(productRepoFilePathInVmr, "TTT", "BBB");
    }

    protected override async Task CopyReposForCurrentTest()
    {
        var dependenciesMap = new Dictionary<string, List<string>>
        {
            { Constants.InstallerRepoName, new List<string> { Constants.ProductRepoName } },
        };

        await CopyRepoAndCreateVersionFiles(Constants.InstallerRepoName, dependenciesMap);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings =
            [
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = InstallerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
                }
            ],
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }

    private static void CheckFileContents(NativePath filePath, string line1, string line2)
    {
        CheckFileContents(filePath,
            $"""
            File in the test repo
            patches will change the next lines
            {line1}
            {line2}
            end of changes
            """
            );
    }
}
