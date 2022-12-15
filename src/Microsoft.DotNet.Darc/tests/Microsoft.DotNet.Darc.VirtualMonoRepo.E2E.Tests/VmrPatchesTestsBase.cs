// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrPatchesTestsBase : VmrTestsBase
{
    protected string patchFileName = null!;
    protected LocalPath installerPatchesDir = null!;
    protected LocalPath vmrPatchesDir = null!;

    protected VmrPatchesTestsBase(string patchFileName)
    {
        this.patchFileName = patchFileName;
    }

    protected override async Task CopyReposForCurrentTest()
    {
        installerPatchesDir = _installerRepoPath / Constants.PatchesFolderName / Constants.ProductRepoName;
        vmrPatchesDir = _vmrPath / VmrInfo.SourcesDir / Constants.InstallerRepoName / Constants.PatchesFolderName / Constants.ProductRepoName;
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.ProductRepoName);
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.InstallerRepoName);
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, _installerRepoPath / "patches" / Constants.ProductRepoName / patchFileName);
        await GitOperations.CommitAll(_installerRepoPath, "Add patch");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var mappings = new List<SourceMapping>
        {
            new SourceMapping(Constants.InstallerRepoName, _installerRepoPath.Path.Replace("\\", "\\\\")),
            new SourceMapping(Constants.ProductRepoName, _privateRepoPath.Path.Replace("\\", "\\\\"))
        };
        var sm = GenerateSourceMappings(mappings, "src/installer/patches/");

        File.WriteAllText(
            _vmrPath / VmrInfo.SourcesDir / VmrInfo.SourceMappingsFileName, sm);

        await GitOperations.CommitAll(_vmrPath, "Add source mappings");
    }
}
