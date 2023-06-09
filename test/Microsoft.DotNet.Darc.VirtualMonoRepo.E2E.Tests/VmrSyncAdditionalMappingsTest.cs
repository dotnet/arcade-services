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
public class VmrSyncAdditionalMappingsTest : VmrTestsBase
{
    private readonly string _fileName = "special-file.txt";
    private readonly string _fileRelativePath = new NativePath("content") / "special-file.txt";

    [Test]
    public async Task NonSrcContentIsSyncedTest()
    {
        // Initialize the repo

        await InitializeRepoAtLastCommit(Constants.ProductRepoName, ProductRepoPath);

        var expectedFilesFromRepos = new List<LocalPath>
        {
            VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / Constants.GetRepoFileName(Constants.ProductRepoName),
            VmrPath / VmrInfo.SourcesDir / Constants.ProductRepoName / _fileRelativePath,
            VmrPath / _fileName
        };

        var expectedFiles = GetExpectedFilesInVmr(
            VmrPath,
            new[] { Constants.ProductRepoName },
            expectedFilesFromRepos
        );

        CheckDirectoryContents(VmrPath, expectedFiles);
        await GitOperations.CheckAllIsCommitted(VmrPath);

        // Change a file in the mapped folder

        File.WriteAllText(
            ProductRepoPath / _fileRelativePath, 
            "A file with a change that needs to be copied outside of the src folder");
        await GitOperations.CommitAll(ProductRepoPath, "Change file");
        await UpdateRepoToLastCommit(Constants.ProductRepoName, ProductRepoPath);

        CheckFileContents(VmrPath / _fileName, "A file with a change that needs to be copied outside of the src folder");
        await GitOperations.CheckAllIsCommitted(VmrPath);
    }

    protected override async Task CopyReposForCurrentTest()
    {
        await CopyRepoAndCreateVersionDetails(CurrentTestDirectory, Constants.ProductRepoName);
        
        Directory.CreateDirectory(ProductRepoPath / "content");
        File.WriteAllText(
            ProductRepoPath / "content" / "special-file.txt",
            "A file that needs to be copied outside of the src folder");

        await GitOperations.CommitAll(ProductRepoPath, "Add a file in additional mappings folder");
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
            },
            AdditionalMappings = new List<AdditionalMappingSetting>
            {
                new AdditionalMappingSetting
                {
                    Source = new UnixPath(VmrInfo.SourcesDir) / Constants.ProductRepoName / "content",
                    Destination = ""
                }
            }
        };

        sourceMappings.Defaults.Exclude = new[]
        {
            "externals/external-repo/**/*.exe",
            "excluded/*",
            "**/*.dll",
            "**/*.Dll",
        };

        await WriteSourceMappingsInVmr(sourceMappings);
    }
}
