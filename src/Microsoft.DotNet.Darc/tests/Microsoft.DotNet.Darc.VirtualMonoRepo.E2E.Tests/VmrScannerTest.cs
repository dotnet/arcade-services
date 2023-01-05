// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrScannerTest : VmrTestsBase
{
    [Test]
    public async Task VmrScannerFindsNothingTest()
    {
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var list = await CallDarcScan();
        Assert.AreEqual(0, list.Count);
    }

    [Test]
    public async Task VmrScannerVmrPreservedTest()
    {
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var newFilePath = VmrPath / "src" / Constants.ProductRepoName / "src";
        Directory.CreateDirectory(newFilePath);
        File.WriteAllText(newFilePath / "test.dll", "this is a test file");
        File.WriteAllText(newFilePath / ".gitattributes", $"*.dll {VmrInfo.KeepAttribute}");
        await GitOperations.CommitAll(VmrPath, "Commit dll file");

        var list = await CallDarcScan();
        Assert.AreEqual(0, list.Count);
    }

    [Test]
    public async Task VmrScannerFindsFileTest()
    {
        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var newFilePath = VmrPath / "src" / Constants.ProductRepoName / "src";
        Directory.CreateDirectory(newFilePath);
        File.WriteAllText(newFilePath / "test.dll", "this is a test file");
        await GitOperations.CommitAll(VmrPath, "Commit dll file");

        var list = await CallDarcScan();
        Assert.AreEqual(1, list.Count);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);

        await GitOperations.CommitAll(ProductRepoPath, "First commit");
    }

    protected async override Task CopyVmrForCurrentTest()
    {
        CopyDirectory(VmrTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings = new List<SourceMappingSetting>
            {
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
                }
            }
        };

        sourceMappings.Defaults.Exclude = new[]
        {
            "**/*.dll"
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}

