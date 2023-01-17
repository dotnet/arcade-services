// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using FluentAssertions;
using Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.VirtualMonoRepo;

[TestFixture]
public class VmrBinaryFileScannerTest : VmrTestsBase
{
    [Test]
    public async Task VmrBinaryFileScannerTests()
    {
        var testFileName = "test.jpg";

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        // Test the scanner when there are no cloacked files to be found
        var list = await CallDarcBinaryFileScan();

        list.Count().Should().Be(0);

        var newFilePath = VmrPath / "src" / Constants.ProductRepoName / "src";
        Directory.CreateDirectory(newFilePath);
        using var bitmap = new Bitmap(3, 3);
        bitmap.Save(newFilePath / testFileName, ImageFormat.Jpeg);
        await GitOperations.CommitAll(VmrPath, "Commit dll file");

        // Test the scanner when there is a cloacked file to be found
        list = await CallDarcBinaryFileScan();

        list.Should().HaveCount(1);
        var path = new NativePath(list.First());
        path.Should().BeEquivalentTo(new NativePath(Path.Join("src", Constants.ProductRepoName, "src", testFileName)));
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);
    }

    protected override async Task CopyVmrForCurrentTest()
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
