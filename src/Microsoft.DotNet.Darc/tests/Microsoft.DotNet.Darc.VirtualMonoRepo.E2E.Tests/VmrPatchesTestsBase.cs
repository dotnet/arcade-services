// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

public class VmrPatchesTestsBase : VmrTestsBase
{
    protected string patchFileName = null!;
    protected readonly string installerRepoName = "installer";

    protected VmrPatchesTestsBase(string patchFileName)
    {
        this.patchFileName = patchFileName;
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, Constants.TestRepoName);
        await CopyRepoAndCreateVersionDetails(_currentTestDirectory, installerRepoName);
        File.Copy(VmrTestsOneTimeSetUp.ResourcesPath / patchFileName, _installerRepoPath / "patches" / Constants.TestRepoName / patchFileName);
        await GitOperations.CommitAll(_installerRepoPath, "Add patch");
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, _vmrPath);

        var mappings = new List<SourceMapping>
        {
            new SourceMapping(installerRepoName, _installerRepoPath.Path.Replace("\\", "\\\\")),
            new SourceMapping(Constants.TestRepoName, _privateRepoPath.Path.Replace("\\", "\\\\"))
        };
        var sm = GenerateSourceMappings(mappings, "src/installer/patches/");

        File.WriteAllText(
            _vmrPath / "src" / "source-mappings.json", sm);

        await GitOperations.CommitAll(_vmrPath, "Add source mappings");
    }
}
