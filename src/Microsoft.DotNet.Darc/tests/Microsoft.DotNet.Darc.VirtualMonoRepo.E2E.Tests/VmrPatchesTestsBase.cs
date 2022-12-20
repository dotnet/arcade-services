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
        installerPatchesDir = _installerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName;
        vmrPatchesDir = _vmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName;
        installerFilePath = _vmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.InstallerRepoFileName;
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.ProductRepoName);
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.InstallerRepoName);
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, _installerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName / patchFileName);
        await GitOperations.CommitAll(_installerRepoPath, "Add patch");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var sourceMappings = new SourceMappingFile
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.InstallerRepoName,
                    DefaultRemote = _installerRepoPath
                },
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = _privateRepoPath
                }
            },
            PatchesPath = "src/installer/patches/"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
