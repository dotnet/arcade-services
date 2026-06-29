// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

[TestFixture]
internal class CloakedFileScannerTests : CodeFlowTestsBase
{
    [Test]
    public async Task VmrCloakedFileScannerTests()
    {
        var testFileName = "test.dll";
        var baselinesFilePath = CodeflowTestsOneTimeSetUp.TestsDirectory / "baselineFiles.txt";
        File.Create(baselinesFilePath).Close();

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        // Test the scanner when there are no cloaked files to be found
        var list = await CallDarcCloakedFileScan(baselinesFilePath);

        list.Should().BeEmpty();

        var newFilePath = VmrPath / "src" / Constants.ProductRepoName / "src";
        Directory.CreateDirectory(newFilePath);
        File.WriteAllText(newFilePath / testFileName, "this is a test file");
        await GitOperations.CommitAll(VmrPath, "Commit dll file");

        // Test the scanner when there is a cloaked file to be found
        list = await CallDarcCloakedFileScan(baselinesFilePath);

        list.Should().HaveCount(1);
        var path = new NativePath(list.First());
        path.Should().BeEquivalentTo(new NativePath(Path.Join("src", Constants.ProductRepoName, "src", testFileName)));

        File.WriteAllText(newFilePath / ".gitattributes", $"*.dll {VmrInfo.KeepAttribute}");
        await GitOperations.CommitAll(VmrPath, "Commit .gitattributes file");

        // Test the scanner when the .gitattributes file is preserving the cloaked file
        list = await CallDarcCloakedFileScan(baselinesFilePath);

        list.Count.Should().Be(0);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionFiles(Constants.ProductRepoName);
    }

    protected override async Task CopyVmrForCurrentTest()
    {
        CopyDirectory(CodeflowTestsOneTimeSetUp.CommonVmrPath, VmrPath);

        var sourceMappings = new SourceMappingFile()
        {
            Mappings =
            [
                new SourceMappingSetting
                {
                    Name = Constants.ProductRepoName,
                    DefaultRemote = ProductRepoPath
                }
            ]
        };

        sourceMappings.Defaults.Exclude =
        [
            "**/*.dll"
        ];

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
