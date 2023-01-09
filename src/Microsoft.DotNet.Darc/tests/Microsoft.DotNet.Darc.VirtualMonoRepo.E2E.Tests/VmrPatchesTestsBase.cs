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
    protected string PatchFileName { get; private set; } = null!;
    protected LocalPath InstallerPatchesDir { get; private set; } = null!;
    protected LocalPath InstallerFilePathInVmr { get; private set; } = null!;

    protected LocalPath ProductRepoFilePathInVmr { get; private set; } = null!;
    protected LocalPath VmrPatchesDir { get; private set; } = null!;

    protected VmrPatchesTestsBase(string PatchFileName)
    {
        this.PatchFileName = PatchFileName;
    }

    protected override async Task CopyReposForCurrentTest()
    {
        InstallerPatchesDir = InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName;
        var vmrSourcesDir = VmrPath / VmrInfo.SourcesDir;
        VmrPatchesDir = vmrSourcesDir / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName;
        InstallerFilePathInVmr = vmrSourcesDir / Constants.InstallerRepoName / Constants.GetRepoFileName(Constants.InstallerRepoName);
        ProductRepoFilePathInVmr = vmrSourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName);
        
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.InstallerRepoName);
        File.Copy(
            VmrTestsOneTimeSetUp.ResourcesPath / PatchFileName, 
            InstallerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName / PatchFileName);
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
