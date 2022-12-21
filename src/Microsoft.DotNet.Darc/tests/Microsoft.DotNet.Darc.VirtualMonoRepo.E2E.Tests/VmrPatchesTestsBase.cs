// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;


namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrPatchesTestsBase : VmrTestsBase
{
    protected string patchFileName = null!;
    protected LocalPath installerPatchesDir = null!;
    protected LocalPath installerFilePath = null!;
    protected LocalPath vmrPatchesDir = null!;

    protected VmrPatchesTestsBase(string patchFileName)
    {
        this.patchFileName = patchFileName;
    }

    protected override async Task CopyReposForCurrentTest()
    {
        installerPatchesDir = InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName;
        vmrPatchesDir = VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName;
        installerFilePath = VmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName);
        await GitOperations.CommitAll(InstallerRepoPath, "Add patch");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings = new List<SourceMappingSetting>
            {
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
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
